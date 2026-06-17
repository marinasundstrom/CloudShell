namespace CloudShell.Abstractions.ResourceManager;

public static class ResourceStandardViewIds
{
    public static ResourceViewId Overview { get; } = new(ResourceTabGroupIds.General, "overview");
    public static ResourceViewId Configuration { get; } = new(ResourceTabGroupIds.General, "configuration");
    public static ResourceViewId Endpoints { get; } = new(ResourceTabGroupIds.Networking, "endpoints");
    public static ResourceViewId Dns { get; } = new(ResourceTabGroupIds.Networking, "dns");
    public static ResourceViewId Identity { get; } = new(ResourceTabGroupIds.Management, "identity");
    public static ResourceViewId Volumes { get; } = new(ResourceTabGroupIds.Storage, "volumes");
    public static ResourceViewId Activity { get; } = new(ResourceTabGroupIds.Management, "activity");
    public static ResourceViewId Environment { get; } = new(ResourceTabGroupIds.Environment, "environment");
    public static ResourceViewId Storage { get; } = new(ResourceTabGroupIds.Storage, "storage");
}
