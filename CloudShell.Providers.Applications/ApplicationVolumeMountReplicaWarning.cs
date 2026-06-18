using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

public static class ApplicationVolumeMountReplicaWarning
{
    public const string Message =
        "Replicas are enabled. One or more mounted volumes do not advertise access that is compatible with every replica mounting the volume.";

    public static bool ShouldShow(
        bool replicasEnabled,
        IEnumerable<ApplicationVolumeMountInput> mounts,
        IReadOnlyList<Resource> volumeResources) =>
        ShouldShow(
            replicasEnabled,
            mounts
                .Where(mount =>
                    !string.IsNullOrWhiteSpace(mount.VolumeReference) &&
                    !string.IsNullOrWhiteSpace(mount.TargetPath))
                .Select(mount => new ResourceVolumeMount(
                    mount.VolumeReference!.Trim(),
                    mount.TargetPath!.Trim(),
                    mount.ReadOnly)),
            volumeResources);

    public static bool ShouldShow(
        bool replicasEnabled,
        IEnumerable<ResourceVolumeMount> mounts,
        IReadOnlyList<Resource> volumeResources) =>
        replicasEnabled &&
        mounts.Any(mount => IsReplicaIncompatibleMount(mount, volumeResources));

    public static bool ShouldShow(
        ApplicationResourceDefinition application,
        IEnumerable<ApplicationVolumeMountInput> mounts,
        IReadOnlyList<Resource> volumeResources) =>
        ApplicationResourceTypes.IsContainerApp(application.ResourceType) &&
        ShouldShow(application.ReplicasEnabled, mounts, volumeResources);

    public static bool ShouldShow(
        ApplicationResourceDefinition application,
        IReadOnlyList<Resource> volumeResources) =>
        ApplicationResourceTypes.IsContainerApp(application.ResourceType) &&
        ShouldShow(application.ReplicasEnabled, application.VolumeMounts, volumeResources);

    private static bool IsReplicaIncompatibleMount(
        ResourceVolumeMount mount,
        IReadOnlyList<Resource> volumeResources)
    {
        var volume = volumeResources.FirstOrDefault(resource =>
            string.Equals(resource.Id, mount.NormalizedVolumeReference, StringComparison.OrdinalIgnoreCase));
        if (volume is null)
        {
            return false;
        }

        var accessMode = GetAccessMode(volume);
        return mount.ReadOnly
            ? accessMode == VolumeAccessMode.ReadWriteOnce
            : accessMode != VolumeAccessMode.ReadWriteMany;
    }

    private static VolumeAccessMode GetAccessMode(Resource volume) =>
        volume.ResourceAttributes.TryGetValue(ResourceAttributeNames.VolumeAccessMode, out var value) &&
        Enum.TryParse<VolumeAccessMode>(value, ignoreCase: true, out var accessMode)
            ? accessMode
            : VolumeAccessMode.ReadWriteOnce;
}
