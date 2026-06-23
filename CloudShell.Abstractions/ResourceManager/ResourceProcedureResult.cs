namespace CloudShell.Abstractions.ResourceManager;

public sealed record ResourceProcedureResult
{
    public ResourceProcedureResult(
        string message,
        bool restartRequired = false,
        string? restartResourceId = null,
        string? restartMessage = null,
        bool runtimeReconciliationRequired = false,
        string? runtimeReconciliationResourceId = null,
        string? runtimeReconciliationMessage = null,
        IReadOnlyList<ResourceProcedureSignal>? signals = null)
    {
        Message = message;
        RestartRequired = restartRequired;
        RestartResourceId = restartResourceId;
        RestartMessage = restartMessage;
        RuntimeReconciliationRequired = runtimeReconciliationRequired;
        RuntimeReconciliationResourceId = runtimeReconciliationResourceId;
        RuntimeReconciliationMessage = runtimeReconciliationMessage;
        Signals = signals ?? [];
    }

    public string Message { get; init; }

    public bool RestartRequired { get; init; }

    public string? RestartResourceId { get; init; }

    public string? RestartMessage { get; init; }

    public bool RuntimeReconciliationRequired { get; init; }

    public string? RuntimeReconciliationResourceId { get; init; }

    public string? RuntimeReconciliationMessage { get; init; }

    public IReadOnlyList<ResourceProcedureSignal> Signals { get; init; }

    public static ResourceProcedureResult Completed(string message) => new(message);

    public static ResourceProcedureResult CompletedWithRestartRequired(
        string message,
        string resourceId,
        string? restartMessage = null) =>
        new(message, true, resourceId, restartMessage);

    public static ResourceProcedureResult CompletedWithRuntimeReconciliationRequired(
        string message,
        string resourceId,
        string? runtimeReconciliationMessage = null) =>
        new(
            message,
            runtimeReconciliationRequired: true,
            runtimeReconciliationResourceId: resourceId,
            runtimeReconciliationMessage: runtimeReconciliationMessage);

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
        var runtimeReconciliation = results.FirstOrDefault(result => result.RuntimeReconciliationRequired);
        return new ResourceProcedureResult(
            message,
            restartRequired: restart is not null,
            restartResourceId: restart?.RestartResourceId,
            restartMessage: restart?.RestartMessage,
            runtimeReconciliationRequired: runtimeReconciliation is not null,
            runtimeReconciliationResourceId: runtimeReconciliation?.RuntimeReconciliationResourceId,
            runtimeReconciliationMessage: runtimeReconciliation?.RuntimeReconciliationMessage,
            signals: signals);
    }
}
