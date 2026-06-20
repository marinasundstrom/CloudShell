namespace CloudShell.Abstractions.ResourceManager;

public static class ResourceManagerRoutes
{
    public const string Resources = "/resources";

    public const string ResourceGraph = "/resources/graph";

    public const string AddResource = "/resources/add";

    public const string CreateResourceGroup = "/resources/groups/new";

    public const string ResourceTemplates = "/resources/templates";

    public const string ResourceSettings = "/resources/settings";

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

    public static string ResourceNotFound(string resourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        return $"/resources/not-found?resourceId={Uri.EscapeDataString(resourceId)}";
    }
}
