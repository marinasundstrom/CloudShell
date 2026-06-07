using System.Text.Json;

namespace CloudShell.Abstractions.ResourceManager;

public sealed record ResourceGroupTemplate(
    string TemplateVersion,
    string Kind,
    string Name,
    string? Description,
    IReadOnlyList<ResourceTemplateDefinition> Resources);

public sealed record ResourceTemplateDefinition(
    string Name,
    string ProviderId,
    string ResourceType,
    IReadOnlyList<string> DependsOn,
    string ProviderConfigurationVersion,
    JsonElement Configuration,
    string? ResourceId = null);

public sealed record ResourceTemplateExportContext(
    ResourceRegistration Registration,
    ResourceGroup? ResourceGroup);

public sealed record ResourceTemplateImportContext(
    string ResourceGroupId,
    IResourceRegistrationStore Registrations,
    IReadOnlyList<string> DependsOn);

public sealed record ResourceTemplateImportResult(
    string ResourceId,
    string Message);

public sealed record ResourceGroupTemplateExportResult(
    ResourceGroupTemplate Template,
    IReadOnlyList<ResourceTemplateDiagnostic> Diagnostics);

public sealed record ResourceGroupTemplateImportResult(
    ResourceGroup ResourceGroup,
    IReadOnlyList<ResourceTemplateImportResult> ImportedResources,
    IReadOnlyList<ResourceTemplateDiagnostic> Diagnostics);

public sealed record ResourceTemplateDiagnostic(
    string Severity,
    string ResourceName,
    string Message)
{
    public static ResourceTemplateDiagnostic Warning(string resourceName, string message) =>
        new("Warning", resourceName, message);

    public static ResourceTemplateDiagnostic Error(string resourceName, string message) =>
        new("Error", resourceName, message);
}

public interface IResourceTemplateProvider
{
    bool CanExport(CloudResource resource);

    Task<ResourceTemplateDefinition> ExportAsync(
        CloudResource resource,
        ResourceTemplateExportContext context,
        CancellationToken cancellationToken = default);

    bool CanImport(ResourceTemplateDefinition template);

    Task<ResourceTemplateImportResult> ImportAsync(
        ResourceTemplateDefinition template,
        ResourceTemplateImportContext context,
        CancellationToken cancellationToken = default);
}
