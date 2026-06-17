namespace CloudShell.Abstractions.ResourceManager;

public static class ResourceManagerRoutes
{
    public const string Resources = "/resources";

    public static string ResourceDetails(string resourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        return $"/resources/{Uri.EscapeDataString(resourceId)}/details";
    }

    public static string ResourceDetails(string resourceId, ResourceViewId viewId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        return $"{ResourceDetails(resourceId)}?tab={Uri.EscapeDataString(viewId.Value)}";
    }

    public static string ResourceOverview(string resourceId) =>
        ResourceDetails(resourceId, ResourcePredefinedViewIds.Overview);
}
