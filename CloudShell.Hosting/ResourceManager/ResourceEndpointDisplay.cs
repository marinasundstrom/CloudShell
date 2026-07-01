using CloudShell.Abstractions.ResourceManager;
using CloudShell.Components;

namespace CloudShell.Hosting.ResourceManager;

internal static class ResourceEndpointDisplay
{
    public static string? GetPreferredEndpointText(
        Resource resource,
        IReadOnlyList<Resource> relatedResources)
    {
        var nameMapping = relatedResources
            .Where(ResourceNameMappingDisplay.IsNameMappingResource)
            .Where(mapping => ResourceNameMappingDisplay.TargetsResource(mapping, resource.Id))
            .Select(mapping => new
            {
                Mapping = mapping,
                Address = GetNameMappingEndpointAddress(mapping, relatedResources)
            })
            .Where(mapping => !string.IsNullOrWhiteSpace(mapping.Address))
            .OrderBy(mapping => ResourceNameMappingDisplay.GetHostName(mapping.Mapping), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (nameMapping is not null)
        {
            return ResourceNameMappingDisplay.GetHostName(nameMapping.Mapping);
        }

        return string.Equals(resource.PrimaryEndpoint, "none", StringComparison.OrdinalIgnoreCase)
            ? null
            : resource.PrimaryEndpoint;
    }

    private static string? GetNameMappingEndpointAddress(
        Resource mapping,
        IReadOnlyList<Resource> relatedResources)
    {
        var targetResourceId = ResourceNameMappingDisplay.GetTargetResourceId(mapping);
        var target = string.IsNullOrWhiteSpace(targetResourceId)
            ? null
            : relatedResources.FirstOrDefault(resource =>
                string.Equals(resource.Id, targetResourceId, StringComparison.OrdinalIgnoreCase));

        return ResourceNameMappingDisplay.GetMappedEndpointAddress(mapping, target);
    }
}
