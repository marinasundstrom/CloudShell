namespace CloudShell.Abstractions.ResourceManager;

public sealed record ResourcePredefinedViewDefinition(
    ResourceViewId Id,
    string Title,
    bool SupportsReplacement = true,
    bool SupportsSections = false);

public static class ResourcePredefinedViews
{
    private static readonly ResourcePredefinedViewDefinition[] Definitions =
    [
        new(ResourcePredefinedViewIds.Overview, "Overview", SupportsSections: false),
        new(ResourcePredefinedViewIds.Configuration, "Configuration", SupportsSections: false),
        new(ResourcePredefinedViewIds.Endpoints, "Endpoints", SupportsSections: true),
        new(ResourcePredefinedViewIds.Dns, "DNS", SupportsSections: true),
        new(ResourcePredefinedViewIds.Identity, "Identity", SupportsSections: false),
        new(ResourcePredefinedViewIds.Volumes, "Volumes", SupportsSections: false),
        new(ResourcePredefinedViewIds.Activity, "Activity", SupportsSections: false),
        new(ResourcePredefinedViewIds.Environment, "Environment", SupportsSections: false),
        new(ResourcePredefinedViewIds.Storage, "Storage", SupportsSections: false)
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
