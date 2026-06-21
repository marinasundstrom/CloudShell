namespace CloudShell.Abstractions.ResourceManager;

public static class ResourceDisplayLabels
{
    public static string GetLabel(Resource resource) =>
        GetLabel(resource, GetName(resource));

    public static string GetLabel(Resource? resource, string fallback) =>
        resource is null
            ? fallback
            : string.IsNullOrWhiteSpace(resource.EffectiveDisplayName)
                ? fallback
                : resource.EffectiveDisplayName.Trim();

    public static string GetLabel(IEnumerable<Resource> resources, string resourceId) =>
        resources.FirstOrDefault(resource => string.Equals(resource.Id, resourceId, StringComparison.OrdinalIgnoreCase)) is { } resource
            ? GetLabel(resource)
            : GetName(resourceId);

    public static string GetName(Resource resource) =>
        !string.IsNullOrWhiteSpace(resource.Name)
            ? resource.Name.Trim()
            : GetName(resource.Id);

    public static string GetName(string resourceId) =>
        ResourceId.TryParse(resourceId, out var parsedResourceId) &&
        !string.IsNullOrWhiteSpace(parsedResourceId.Name)
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
