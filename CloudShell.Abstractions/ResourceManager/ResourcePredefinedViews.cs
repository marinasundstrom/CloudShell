namespace CloudShell.Abstractions.ResourceManager;

public sealed record ResourcePredefinedViewDefinition(
    ResourceViewId Id,
    string Title,
    bool SupportsReplacement = true,
    bool SupportsSections = false,
    string? Icon = null);

public static class ResourcePredefinedViews
{
    private static readonly ResourcePredefinedViewDefinition[] Definitions =
    [
        new(ResourcePredefinedViewIds.Overview, "Overview", SupportsSections: true, Icon: "overview"),
        new(ResourcePredefinedViewIds.Configuration, "Configuration", SupportsSections: false, Icon: "configuration"),
        new(ResourcePredefinedViewIds.Endpoints, "Endpoints", SupportsSections: true, Icon: "endpoints"),
        new(ResourcePredefinedViewIds.Dns, "DNS", SupportsSections: true, Icon: "dns"),
        new(ResourcePredefinedViewIds.Identity, "Identity", SupportsSections: true, Icon: "identity"),
        new(ResourcePredefinedViewIds.AccessControl, "Access control", SupportsSections: true, Icon: "permissions"),
        new(ResourcePredefinedViewIds.Volumes, "Volumes", SupportsSections: false, Icon: "volumes"),
        new(ResourcePredefinedViewIds.Activity, "Activity", SupportsSections: true, Icon: "activity"),
        new(ResourcePredefinedViewIds.Health, "Health", SupportsSections: true, Icon: "health"),
        new(ResourcePredefinedViewIds.Recovery, "Recovery", SupportsSections: true, Icon: "recovery"),
        new(ResourcePredefinedViewIds.Monitoring, "Monitoring", SupportsSections: true, Icon: "monitoring"),
        new(ResourcePredefinedViewIds.Usage, "Usage", SupportsSections: true, Icon: "usage"),
        new(ResourcePredefinedViewIds.Environment, "Environment", SupportsSections: false, Icon: "environment"),
        new(ResourcePredefinedViewIds.Logs, "Logs", SupportsSections: false, Icon: "document"),
        new(ResourcePredefinedViewIds.Traces, "Traces", SupportsSections: false, Icon: "traces"),
        new(ResourcePredefinedViewIds.Metrics, "Metrics", SupportsSections: false, Icon: "metrics"),
        new(ResourcePredefinedViewIds.Storage, "Storage", SupportsSections: false, Icon: "storage")
    ];

    private static readonly IReadOnlyDictionary<ResourceViewId, ResourcePredefinedViewDefinition> DefinitionsById =
        Definitions.ToDictionary(
            definition => definition.Id,
            definition => definition);

    public static IReadOnlyList<ResourcePredefinedViewDefinition> All => Definitions;

    public static bool TryGet(
        ResourceViewId id,
        out ResourcePredefinedViewDefinition definition) =>
        DefinitionsById.TryGetValue(id, out definition!);

    public static ResourcePredefinedViewDefinition Get(ResourceViewId id) =>
        TryGet(id, out var definition)
            ? definition
            : throw new InvalidOperationException(
                $"'{id}' is not a known predefined resource view.");
}
