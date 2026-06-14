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
            ? resource.Name
            : $"{resource.Name} ({medium})";
    }

    private static string GetVolumeStorageMedium(Resource resource) =>
        resource.ResourceAttributes.TryGetValue(ResourceAttributeNames.VolumeStorageMedium, out var medium)
            ? medium
            : string.Empty;
}
