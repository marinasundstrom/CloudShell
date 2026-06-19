using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Components;

public static class ResourceStateDisplay
{
    public static string GetStateClass(ResourceState? state) =>
        state switch
        {
            ResourceState.Running => "state-running",
            ResourceState.Starting => "state-starting",
            ResourceState.Stopping => "state-stopping",
            ResourceState.Paused => "state-paused",
            ResourceState.Degraded => "state-degraded",
            ResourceState.Stopped => "state-stopped",
            _ => "state-unknown"
        };
}
