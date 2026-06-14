using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Hosting.ResourceManager;

public static class ResourceLifecycleDisplay
{
    public static bool ShouldShowStatus(Resource resource) => resource.State.HasValue;
}
