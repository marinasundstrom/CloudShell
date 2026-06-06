namespace CloudShell.Abstractions.ResourceManager;

public sealed class ResourceConfigurationContext(string resourceId)
{
    private Func<CancellationToken, Task<ResourceProcedureResult>>? applyHandler;

    public string ResourceId { get; } = resourceId;

    public bool CanApply => applyHandler is not null;

    public event Action? StateChanged;

    public void SetApplyHandler(Func<CancellationToken, Task<ResourceProcedureResult>> handler)
    {
        applyHandler = handler;
        StateChanged?.Invoke();
    }

    public Task<ResourceProcedureResult> ApplyAsync(CancellationToken cancellationToken = default) =>
        applyHandler?.Invoke(cancellationToken) ??
        Task.FromResult(ResourceProcedureResult.Completed("No changes to apply."));
}
