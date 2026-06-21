using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

internal static class ApplicationVolumeResourceDisplay
{
    public static bool IsMountableVolumeResource(Resource resource) =>
        string.Equals(resource.EffectiveTypeId, "cloudshell.volume", StringComparison.OrdinalIgnoreCase) ||
        resource.HasCapability(ResourceCapabilityIds.StorageVolume);

    public static string GetVolumeOptionLabel(Resource resource)
    {
        var medium = GetVolumeStorageMedium(resource);
        return string.IsNullOrWhiteSpace(medium)
            ? ResourceDisplayLabels.GetLabel(resource)
            : $"{ResourceDisplayLabels.GetLabel(resource)} ({medium})";
    }

    public static string GetMountSourceLabel(ResourceVolumeMount mount, Resource? volume) =>
        volume is null
            ? mount.NormalizedVolumeReference
            : GetVolumeOptionLabel(volume);

    public static string GetMountSummary(ResourceVolumeMount mount, Resource? volume) =>
        $"{GetMountSourceLabel(mount, volume)} -> {mount.NormalizedTargetPath}";

    public static string GetMountAccessLabel(ResourceVolumeMount mount) =>
        GetMountAccessLabel(mount, static value => value);

    public static string GetMountAccessLabel(ResourceVolumeMount mount, Func<string, string> localize) =>
        mount.ReadOnly ? localize("Read-only") : localize("Read/write");

    private static string GetVolumeStorageMedium(Resource resource) =>
        resource.ResourceAttributes.TryGetValue(ResourceAttributeNames.VolumeStorageMedium, out var medium)
            ? medium
            : string.Empty;

}
