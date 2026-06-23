namespace CloudShell.Abstractions.ResourceManager;

public static class ResourceManagerRoutes
{
    public const string Resources = "/resources";

    public const string ResourceGraph = "/resources/graph";

    public const string Environment = "/environment";

    public const string AddResource = "/resources/add";

    public const string CreateResourceGroup = "/resources/groups/new";

    public const string ResourceTemplates = "/resources/templates";

    public const string ResourceSettings = "/resources/settings";

    public static string ResourceDetails(string resourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        return $"/resources/{Uri.EscapeDataString(resourceId)}";
    }

    public static string ResourceDetails(string resourceId, ResourceViewId viewId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        if (viewId == ResourcePredefinedViewIds.Overview)
        {
            return ResourceDetails(resourceId);
        }

        return $"{ResourceDetails(resourceId)}/{Uri.EscapeDataString(GetResourceViewSegment(viewId))}";
    }

    public static string ResourceOverview(string resourceId) =>
        ResourceDetails(resourceId);

    public static string GetResourceViewSegment(ResourceViewId viewId) =>
        viewId.Identifier;

    public static string ResourceNotFound(string resourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        return $"/resources/not-found?resourceId={Uri.EscapeDataString(resourceId)}";
    }
}
