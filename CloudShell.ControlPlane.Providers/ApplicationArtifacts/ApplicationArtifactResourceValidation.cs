using System.IO.Compression;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ResourceModel;

namespace CloudShell.ControlPlane.Providers;

internal static class ApplicationArtifactResourceValidation
{
    public static bool UsesUploadedArtifact(ResourceAttributeValueMap attributes) =>
        HasApplicationArtifactsEnabled(attributes) ||
        string.Equals(
            GetString(attributes, ApplicationArtifactAttributeIds.SourceKind),
            DeploymentArtifactSourceKinds.UploadedArtifact,
            StringComparison.OrdinalIgnoreCase);

    public static bool UsesUploadedArtifact(ResourceAttributeSet attributes) =>
        HasApplicationArtifactsEnabled(attributes) ||
        string.Equals(
            attributes.GetString(ApplicationArtifactAttributeIds.SourceKind),
            DeploymentArtifactSourceKinds.UploadedArtifact,
            StringComparison.OrdinalIgnoreCase);

    public static bool IsResourceManagerUiOwned(CloudShell.Abstractions.ResourceManager.Resource resource) =>
        string.Equals(
            resource.ResourceAttributes.GetValueOrDefault(ResourceAttributeNames.ApplicationSourceOwner),
            ApplicationArtifactAttributeIds.ResourceManagerUiSourceOwner,
            StringComparison.OrdinalIgnoreCase);

    public static string? GetLifecycleUnavailableReason(
        Resource resource,
        ResourceOperationId operationId)
    {
        return null;
    }

    public static void ValidateSource(
        ResourceAttributeValueMap attributes,
        ResourceAttributeId localPathAttribute,
        string localPathRequiredCode,
        string localPathRequiredMessage,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (UsesUploadedArtifact(attributes))
        {
            ValidateUploadedArtifact(attributes, diagnostics);
            return;
        }

        ValidateLocalPath(
            GetString(attributes, localPathAttribute),
            localPathAttribute,
            localPathRequiredCode,
            localPathRequiredMessage,
            diagnostics);
    }

    public static void ValidateSource(
        ResourceAttributeSet attributes,
        ResourceAttributeId localPathAttribute,
        string localPathRequiredCode,
        string localPathRequiredMessage,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (UsesUploadedArtifact(attributes))
        {
            ValidateUploadedArtifact(attributes, diagnostics);
            return;
        }

        ValidateLocalPath(
            attributes.GetString(localPathAttribute),
            localPathAttribute,
            localPathRequiredCode,
            localPathRequiredMessage,
            diagnostics);
    }

    public static void ValidateUploadedArtifact(
        ResourceAttributeValueMap attributes,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        var artifact = attributes.GetObject<ApplicationArtifactReference>(
            ApplicationArtifactAttributeIds.Source);
        if (artifact is null)
        {
            return;
        }

        ValidateUploadedArtifactReference(artifact, diagnostics);
    }

    public static void ValidateUploadedArtifact(
        ResourceAttributeSet attributes,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        var artifact = attributes.GetObject<ApplicationArtifactReference>(
            ApplicationArtifactAttributeIds.Source);
        if (artifact is null)
        {
            return;
        }

        ValidateUploadedArtifactReference(artifact, diagnostics);
    }

    public static ResourceDefinitionValidationResult ValidateCommittedArtifact(
        DeploymentArtifactValidationContext context,
        Stream artifactContent)
    {
        if (artifactContent.CanSeek && artifactContent.Length == 0)
        {
            return ResourceDefinitionValidationResult.FromDiagnostics(
            [
                ResourceDefinitionDiagnostic.Error(
                    "application.artifact.empty",
                    "Application artifact package is empty.",
                    context.ArtifactId)
            ]);
        }

        if (string.Equals(
                context.ResourceType,
                AspNetCoreProjectResourceTypeProvider.ResourceTypeId.ToString(),
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                context.ArtifactLayoutKind,
                "dotnetPublishedOutput",
                StringComparison.OrdinalIgnoreCase))
        {
            return ValidateDotNetPublishedOutput(context, artifactContent);
        }

        return ResourceDefinitionValidationResult.FromDiagnostics([]);
    }

    private static ResourceDefinitionValidationResult ValidateDotNetPublishedOutput(
        DeploymentArtifactValidationContext context,
        Stream artifactContent)
    {
        if (!string.Equals(context.PackageKind, "zip", StringComparison.OrdinalIgnoreCase))
        {
            return ResourceDefinitionValidationResult.FromDiagnostics(
            [
                ResourceDefinitionDiagnostic.Error(
                    "application.aspNetCoreProject.artifactPackageKindUnsupported",
                    ".NET published output artifacts must be uploaded as a ZIP package.",
                    context.ArtifactId)
            ]);
        }

        try
        {
            using var archive = new ZipArchive(artifactContent, ZipArchiveMode.Read, leaveOpen: false);
            return ContainsPublishedOutputAssembly(archive, context.EntryPath)
                ? ResourceDefinitionValidationResult.FromDiagnostics([])
                : ResourceDefinitionValidationResult.FromDiagnostics(
                [
                    ResourceDefinitionDiagnostic.Error(
                        "application.aspNetCoreProject.publishedAssemblyMissing",
                        ".NET published output artifacts must contain an application runtimeconfig.json file and matching DLL.",
                        context.ArtifactId)
                ]);
        }
        catch (InvalidDataException exception)
        {
            return ResourceDefinitionValidationResult.FromDiagnostics(
            [
                ResourceDefinitionDiagnostic.Error(
                    "application.artifact.invalidZip",
                    $"Application artifact ZIP package could not be read: {exception.Message}",
                    context.ArtifactId)
            ]);
        }
    }

    private static bool ContainsPublishedOutputAssembly(
        ZipArchive archive,
        string? entryPath)
    {
        var prefix = NormalizeZipPrefix(entryPath);
        var entries = archive.Entries
            .Where(entry => !string.IsNullOrEmpty(entry.Name))
            .ToArray();

        if (!string.IsNullOrWhiteSpace(entryPath) &&
            entryPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return entries.Any(entry =>
                string.Equals(
                    NormalizeZipPath(entry.FullName),
                    NormalizeZipPath(entryPath),
                    StringComparison.OrdinalIgnoreCase));
        }

        var files = entries
            .Select(entry => NormalizeZipPath(entry.FullName))
            .Where(path => string.IsNullOrEmpty(prefix) ||
                path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var runtimeConfig in files.Where(path =>
            path.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase) &&
            !Path.GetFileName(path).Contains("StaticWebAssets", StringComparison.OrdinalIgnoreCase)))
        {
            var directory = Path.GetDirectoryName(runtimeConfig)?.Replace('\\', '/') ?? string.Empty;
            var assemblyName = Path.GetFileNameWithoutExtension(
                Path.GetFileNameWithoutExtension(runtimeConfig)) + ".dll";
            var assemblyPath = string.IsNullOrEmpty(directory)
                ? assemblyName
                : $"{directory}/{assemblyName}";
            if (files.Contains(assemblyPath))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeZipPrefix(string? entryPath)
    {
        if (string.IsNullOrWhiteSpace(entryPath) ||
            string.Equals(entryPath.Trim(), ".", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var normalized = NormalizeZipPath(entryPath.Trim());
        return normalized.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : normalized.TrimEnd('/') + "/";
    }

    private static string NormalizeZipPath(string value) =>
        value.Replace('\\', '/').TrimStart('/');

    private static void ValidateLocalPath(
        string? projectPath,
        ResourceAttributeId attributeId,
        string code,
        string message,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(code, message, attributeId));
        }
    }

    private static void ValidateUploadedArtifactReference(
        ApplicationArtifactReference? artifact,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (artifact is null)
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                "application.artifact.referenceRequired",
                "Application artifact source metadata is incomplete.",
                ApplicationArtifactAttributeIds.Source));
            return;
        }

        if (string.IsNullOrWhiteSpace(artifact.ArtifactId) ||
            string.IsNullOrWhiteSpace(artifact.RevisionId))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                "application.artifact.revisionRequired",
                "Application artifact mode requires both artifact id and revision id.",
                ApplicationArtifactAttributeIds.Source));
        }

        if (string.IsNullOrWhiteSpace(artifact.PackageKind))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                "application.artifact.packageKindRequired",
                "Application artifact mode requires the package kind accepted by the provider.",
                ApplicationArtifactAttributeIds.Source));
        }

        if (string.IsNullOrWhiteSpace(artifact.ContentSha256))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                "application.artifact.hashRequired",
                "Application artifact mode requires a content hash.",
                ApplicationArtifactAttributeIds.Source));
        }
    }

    private static string? GetString(
        ResourceAttributeValueMap attributes,
        ResourceAttributeId attributeId) =>
        attributes.TryGetValue(attributeId, out var value) &&
        value.TryGetScalarString(out var scalar)
            ? scalar
            : null;

    private static bool HasApplicationArtifactsEnabled(ResourceAttributeValueMap attributes) =>
        attributes.TryGetValue(ApplicationArtifactAttributeIds.Enabled, out var value) &&
        value.BooleanValue == true;

    private static bool HasApplicationArtifactsEnabled(ResourceAttributeSet attributes) =>
        attributes.GetValue(ApplicationArtifactAttributeIds.Enabled)?.BooleanValue == true;
}
