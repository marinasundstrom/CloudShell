namespace CloudShell.ResourceModel;

public sealed record ResourceDefinitionDiagnostic(
    ResourceDefinitionDiagnosticSeverity Severity,
    string Code,
    string Message,
    string? Target = null)
{
    public static ResourceDefinitionDiagnostic Error(
        string code,
        string message,
        string? target = null) =>
        new(ResourceDefinitionDiagnosticSeverity.Error, code, message, target);

    public static ResourceDefinitionDiagnostic Warning(
        string code,
        string message,
        string? target = null) =>
        new(ResourceDefinitionDiagnosticSeverity.Warning, code, message, target);
}

public enum ResourceDefinitionDiagnosticSeverity
{
    Information,
    Warning,
    Error
}

public sealed record ResourceDefinitionValidationResult(
    IReadOnlyList<ResourceDefinitionDiagnostic> Diagnostics)
{
    public static ResourceDefinitionValidationResult Success { get; } = new([]);

    public bool HasErrors => Diagnostics.Any(diagnostic =>
        diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error);

    public static ResourceDefinitionValidationResult FromDiagnostics(
        IEnumerable<ResourceDefinitionDiagnostic> diagnostics) =>
        new(diagnostics.ToArray());
}
