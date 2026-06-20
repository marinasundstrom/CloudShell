using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Hosting.ResourceManager;

public static class ResourceLifecycleDisplay
{
    public static bool ShouldShowStatus(Resource resource) => resource.State.HasValue;

    public static bool HasStateHealthIncongruity(
        Resource resource,
        ResourceHealthSummary? healthSummary) =>
        resource.ResourceHealthChecks.Count > 0 &&
        healthSummary?.Status == ResourceHealthStatus.Healthy &&
        IsNonRunningState(resource.State);

    private static bool IsNonRunningState(ResourceState? state) =>
        state is ResourceState.Stopped or
            ResourceState.Stopping or
            ResourceState.Paused or
            ResourceState.Unknown;
}
