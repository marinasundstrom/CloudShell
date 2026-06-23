using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

internal sealed class ApplicationRuntimeStateTracker(
    IApplicationRuntimeStateStore runtimeStates,
    Func<string, bool> isRunning,
    Func<DateTimeOffset>? utcNow = null,
    TimeSpan? transientStateTimeout = null)
{
    private readonly Func<DateTimeOffset> utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    private readonly TimeSpan transientStateTimeout = transientStateTimeout ?? TimeSpan.FromMinutes(5);

    public ResourceState GetState(string applicationId)
    {
        var now = utcNow();
        var runtimeState = runtimeStates.Get(applicationId);
        if (runtimeState?.State is ResourceState.Starting or ResourceState.Stopping &&
            now - runtimeState.LastObservedAt <= transientStateTimeout)
        {
            return runtimeState.State.Value;
        }

        return isRunning(applicationId)
            ? ResourceState.Running
            : ResourceState.Stopped;
    }

    public void MarkStarting(string applicationId) =>
        SaveTransientState(applicationId, ResourceState.Starting);

    public void ClearStarting(string applicationId)
    {
        var state = runtimeStates.Get(applicationId);
        if (state?.State is not ResourceState.Starting ||
            isRunning(applicationId))
        {
            return;
        }

        runtimeStates.Save(state with
        {
            LastObservedAt = utcNow(),
            State = ResourceState.Stopped
        });
    }

    public void MarkStopping(string applicationId) =>
        SaveTransientState(applicationId, ResourceState.Stopping);

    public void ClearStopping(string applicationId)
    {
        var state = runtimeStates.Get(applicationId);
        if (state?.State is not ResourceState.Stopping ||
            isRunning(applicationId))
        {
            return;
        }

        runtimeStates.Save(state with
        {
            LastObservedAt = utcNow(),
            State = ResourceState.Stopped
        });
    }

    private void SaveTransientState(string applicationId, ResourceState state)
    {
        var now = utcNow();
        var current = runtimeStates.Get(applicationId);
        runtimeStates.Save(current is null
            ? new ApplicationRuntimeState(
                applicationId,
                null,
                null,
                now,
                State: state)
            : current with
            {
                LastObservedAt = now,
                State = state
            });
    }
}
