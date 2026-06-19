using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Components;

public static class ResourceDisplayLabels
{
    public static string GetLabel(Resource resource) =>
        GetLabel(resource, resource.Id);

    public static string GetLabel(Resource? resource, string fallback) =>
        resource is null
            ? fallback
            : !string.IsNullOrWhiteSpace(resource.DisplayName)
                ? resource.DisplayName
                : !string.IsNullOrWhiteSpace(resource.Name)
                    ? resource.Name
                    : fallback;

    public static string GetName(Resource resource) =>
        !string.IsNullOrWhiteSpace(resource.Name)
            ? resource.Name
            : ResourceId.TryParse(resource.Id, out var resourceId)
                ? resourceId.Name
                : resource.Id;

    public static string GetQualifiedLabel(Resource resource)
    {
        var label = GetLabel(resource);
        var name = GetName(resource);

        return string.Equals(label, name, StringComparison.Ordinal)
            ? label
            : $"{label} ({name})";
    }
}
