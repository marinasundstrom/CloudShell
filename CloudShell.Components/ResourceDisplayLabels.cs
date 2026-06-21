using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Components;

public static class ResourceDisplayLabels
{
    public static string GetLabel(Resource resource) =>
        GetLabel(resource, resource.Id);

    public static string GetLabel(Resource? resource, string fallback) =>
        resource is null
            ? fallback
            : string.IsNullOrWhiteSpace(resource.EffectiveDisplayName)
                ? fallback
                : resource.EffectiveDisplayName;

    public static string GetLabel(IEnumerable<Resource> resources, string resourceId) =>
        resources.FirstOrDefault(resource => string.Equals(resource.Id, resourceId, StringComparison.OrdinalIgnoreCase)) is { } resource
            ? GetLabel(resource)
            : resourceId;

    public static string GetName(Resource resource) =>
        !string.IsNullOrWhiteSpace(resource.Name)
            ? resource.Name
            : ResourceId.TryParse(resource.Id, out var resourceId)
                ? resourceId.Name
                : resource.Id;

    public static string GetName(string resourceId) =>
        ResourceId.TryParse(resourceId, out var parsedResourceId)
            ? parsedResourceId.Name
            : resourceId;

    public static string GetQualifiedLabel(Resource resource)
    {
        var label = GetLabel(resource);
        var name = GetName(resource);

        return string.Equals(label, name, StringComparison.Ordinal)
            ? label
            : $"{label} ({name})";
    }
}
