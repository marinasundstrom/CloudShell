using CloudShell.ResourceModel;

namespace CloudShell.Abstractions.ResourceManager;

public sealed record ResourceTemplateExportRequest(
    string Name,
    IReadOnlyList<string>? ResourceIds = null,
    string? EnvironmentId = null,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public IReadOnlyList<string> RequestedResourceIds => ResourceIds ?? [];
}

public sealed record ResourceTemplateExportResult(
    ResourceTemplate Template,
    IReadOnlyList<ResourceDefinitionDiagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(diagnostic =>
        diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error);
}

public sealed record ResourceTemplateApplyResult(
    ResourceTemplate Template,
    bool IsCommitted,
    IReadOnlyList<ResourceDefinitionDiagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(diagnostic =>
        diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error);
}

public sealed record ResourceTemplateApplyRequest(
    ResourceTemplate Template,
    ResourceDefinitionApplyMode Mode = ResourceDefinitionApplyMode.CreateOrUpdate);
