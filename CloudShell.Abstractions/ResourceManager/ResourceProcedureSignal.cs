namespace CloudShell.Abstractions.ResourceManager;

public sealed record ResourceProcedureSignal(
    ResourceSignalSeverity Severity,
    string Message)
{
    public static ResourceProcedureSignal Warning(string message) =>
        new(ResourceSignalSeverity.Warning, message);

    public static ResourceProcedureSignal Error(string message) =>
        new(ResourceSignalSeverity.Error, message);
}
