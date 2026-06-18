using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

public static class ApplicationVolumeMountReplicaWarning
{
    public const string Message =
        "Replicas are enabled. Mounted volumes are attached to every replica; use storage that is safe for concurrent access, or disable replicas before assigning single-writer data.";

    public static bool ShouldShow(
        bool replicasEnabled,
        IEnumerable<ApplicationVolumeMountInput> mounts) =>
        replicasEnabled &&
        mounts.Any(mount =>
            !string.IsNullOrWhiteSpace(mount.VolumeReference) &&
            !string.IsNullOrWhiteSpace(mount.TargetPath));

    public static bool ShouldShow(
        bool replicasEnabled,
        IEnumerable<ResourceVolumeMount> mounts) =>
        replicasEnabled &&
        mounts.Any(mount =>
            !string.IsNullOrWhiteSpace(mount.VolumeReference) &&
            !string.IsNullOrWhiteSpace(mount.TargetPath));

    public static bool ShouldShow(
        ApplicationResourceDefinition application,
        IEnumerable<ApplicationVolumeMountInput> mounts) =>
        ApplicationResourceTypes.IsContainerApp(application.ResourceType) &&
        ShouldShow(application.ReplicasEnabled, mounts);

    public static bool ShouldShow(ApplicationResourceDefinition application) =>
        ApplicationResourceTypes.IsContainerApp(application.ResourceType) &&
        ShouldShow(application.ReplicasEnabled, application.VolumeMounts);
}
