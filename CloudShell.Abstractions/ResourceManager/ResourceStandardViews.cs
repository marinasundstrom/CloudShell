namespace CloudShell.Abstractions.ResourceManager;

public sealed record ResourceStandardViewDefinition(
    string Id,
    string Title,
    bool SupportsReplacement = true,
    bool SupportsSections = false);

public static class ResourceStandardViews
{
    private static readonly ResourceStandardViewDefinition[] Definitions =
    [
        new(ResourceStandardViewIds.Overview, "Overview", SupportsSections: false),
        new(ResourceStandardViewIds.Configuration, "Configuration", SupportsSections: false),
        new(ResourceStandardViewIds.Endpoints, "Endpoints", SupportsSections: true),
        new(ResourceStandardViewIds.Dns, "DNS", SupportsSections: true),
        new(ResourceStandardViewIds.Identity, "Identity", SupportsSections: false),
        new(ResourceStandardViewIds.Volumes, "Volumes", SupportsSections: false),
        new(ResourceStandardViewIds.Activity, "Activity", SupportsSections: false),
        new(ResourceStandardViewIds.Environment, "Environment", SupportsSections: false),
        new(ResourceStandardViewIds.Storage, "Storage", SupportsSections: false)
    ];

    private static readonly IReadOnlyDictionary<string, ResourceStandardViewDefinition> DefinitionsById =
        Definitions.ToDictionary(
            definition => definition.Id,
            StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<ResourceStandardViewDefinition> All => Definitions;

    public static bool TryGet(
        string id,
        out ResourceStandardViewDefinition definition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return DefinitionsById.TryGetValue(id.Trim(), out definition!);
    }

    public static ResourceStandardViewDefinition Get(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return TryGet(id, out var definition)
            ? definition
            : throw new InvalidOperationException(
                $"'{id}' is not a known standard resource view.");
    }
}
