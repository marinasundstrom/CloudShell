namespace CloudShell.Abstractions.ResourceManager;

public sealed record ResourceProcedureResult(
    string Message,
    bool RestartRequired = false,
    string? RestartResourceId = null,
    string? RestartMessage = null)
{
    public static ResourceProcedureResult Completed(string message) => new(message);

    public static ResourceProcedureResult CompletedWithRestartRequired(
        string message,
        string resourceId,
        string? restartMessage = null) =>
        new(message, true, resourceId, restartMessage);
}
