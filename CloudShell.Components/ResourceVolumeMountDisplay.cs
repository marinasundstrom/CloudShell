using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Components;

public static class ResourceVolumeMountDisplay
{
    public static string GetMountSummary(ResourceVolumeMount mount)
    {
        var target = mount.NormalizedTargetPath;
        var access = mount.ReadOnly ? "read-only" : "read/write";

        return string.IsNullOrWhiteSpace(mount.NormalizedName)
            ? $"{target} - {access}"
            : $"{mount.NormalizedName}: {target} - {access}";
    }

    public static string GetMaterializationStatusLabel(string? status) =>
        status switch
        {
            ResourceVolumeMountMaterializationStatus.Materialized => "materialized",
            "partial" => "partially materialized",
            ResourceVolumeMountMaterializationStatus.NotActive => "not active",
            "unknown" => "unknown",
            "notApplicable" => "not applicable",
            { Length: > 0 } value => value,
            _ => "unknown"
        };

    public static string? GetMaterializationSummary(Resource resource)
    {
        if (!resource.ResourceAttributes.TryGetValue(
                ResourceAttributeNames.VolumeMountMaterializationStatus,
                out var status) ||
            string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        var label = GetMaterializationStatusLabel(status);
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
