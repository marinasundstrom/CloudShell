using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Hosting.ResourceManager;

internal static class ResourceTypeGallery
{
    public static IReadOnlyList<ResourceTypeContribution> Filter(IEnumerable<ResourceTypeContribution> types, ResourceClass? category, string? search)
    {
        var terms = (search ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return types.Where(type => category is null || type.ResourceClass == category)
            .Where(type => terms.All(term => $"{type.DisplayName} {type.Description} {type.Id}".Contains(term, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(type => type.Order).ThenBy(type => type.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
