namespace CloudShell.Abstractions.ResourceManager;

public sealed record ResourceProcedureResult
{
    public ResourceProcedureResult(
        string message,
        bool restartRequired = false,
        string? restartResourceId = null,
        string? restartMessage = null,
        IReadOnlyList<ResourceProcedureSignal>? signals = null)
    {
        Message = message;
        RestartRequired = restartRequired;
        RestartResourceId = restartResourceId;
        RestartMessage = restartMessage;
        Signals = signals ?? [];
    }

    public string Message { get; init; }

    public bool RestartRequired { get; init; }

    public string? RestartResourceId { get; init; }

    public string? RestartMessage { get; init; }

    public IReadOnlyList<ResourceProcedureSignal> Signals { get; init; }

    public static ResourceProcedureResult Completed(string message) => new(message);

    public static ResourceProcedureResult CompletedWithRestartRequired(
        string message,
        string resourceId,
        string? restartMessage = null) =>
        new(message, true, resourceId, restartMessage);

    public static ResourceProcedureResult Combine(
        IReadOnlyList<ResourceProcedureResult> results,
        string emptyMessage)
    {
        ArgumentNullException.ThrowIfNull(results);

        if (results.Count == 0)
        {
            return Completed(emptyMessage);
        }

        var message = string.Join(" ", results
            .Select(result => result.Message)
            .Where(message => !string.IsNullOrWhiteSpace(message)));
        var signals = results
            .SelectMany(result => result.Signals)
            .ToArray();
        var restart = results.FirstOrDefault(result => result.RestartRequired);
        return restart is null
            ? new ResourceProcedureResult(message, signals: signals)
            : new ResourceProcedureResult(
                message,
                restartRequired: true,
                restartResourceId: restart.RestartResourceId ?? string.Empty,
                restartMessage: restart.RestartMessage,
                signals: signals);
    }
}
