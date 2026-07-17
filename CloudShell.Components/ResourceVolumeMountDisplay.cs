using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Components;

public static class ResourceVolumeMountDisplay
{
    public static string GetMountSummary(ResourceVolumeMount mount)
    {
        return GetMountSummary(mount, static value => value);
    }

    public static string GetMountSummary(ResourceVolumeMount mount, Func<string, string> localize)
    {
        var target = mount.NormalizedTargetPath;
        var access = mount.ReadOnly
            ? localize("read-only")
            : localize("read/write");

        return string.IsNullOrWhiteSpace(mount.NormalizedName)
            ? $"{target} - {access}"
            : $"{mount.NormalizedName}: {target} - {access}";
    }

    public static string GetMaterializationStatusLabel(string? status) =>
        GetMaterializationStatusLabel(status, static value => value);

    public static string GetMaterializationStatusLabel(string? status, Func<string, string> localize) =>
        status switch
        {
            ResourceVolumeMountMaterializationStatus.Materialized => localize("Active"),
            "partial" => localize("Partially active"),
            ResourceVolumeMountMaterializationStatus.NotActive => localize("Not active"),
            "unknown" => localize("Unknown"),
            "notApplicable" => localize("Not applicable"),
            { Length: > 0 } value => value,
            _ => localize("Unknown")
        };

    public static string? GetMaterializationSummary(Resource resource)
    {
        return GetMaterializationSummary(resource, static value => value);
    }

    public static string? GetMaterializationSummary(Resource resource, Func<string, string> localize)
    {
        if (!resource.ResourceAttributes.TryGetValue(
                ResourceAttributeNames.VolumeMountMaterializationStatus,
                out var status) ||
            string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        var label = GetMaterializationStatusLabel(status, localize);
        if (resource.ResourceAttributes.TryGetValue(
                ResourceAttributeNames.VolumeMountMaterializedCount,
                out var materializedCount) &&
            resource.ResourceAttributes.TryGetValue(
                ResourceAttributeNames.VolumeMountCount,
                out var mountCount))
        {
            label = $"{label} ({materializedCount}/{mountCount})";
        }

        return label;
    }
}
