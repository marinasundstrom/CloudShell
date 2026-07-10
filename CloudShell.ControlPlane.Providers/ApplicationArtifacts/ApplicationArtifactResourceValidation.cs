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

        return ResourceDefinitionValidationResult.FromDiagnostics([]);
    }

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
                "Application artifact mode requires an uploaded artifact revision reference.",
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
