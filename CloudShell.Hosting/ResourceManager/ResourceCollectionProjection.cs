using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Hosting.ResourceManager;

internal static class ResourceCollectionProjection
{
    public static IReadOnlyList<Resource> DistinctById(IEnumerable<Resource> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);

        return resources
            .GroupBy(resource => resource.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    public static IReadOnlyDictionary<string, Resource> ToDictionaryById(IEnumerable<Resource> resources) =>
        DistinctById(resources)
            .ToDictionary(
                resource => resource.Id,
                resource => resource,
                StringComparer.OrdinalIgnoreCase);
}
