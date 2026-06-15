using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

public sealed record ApplicationRuntimeState(
    string ApplicationId,
    int? LastKnownProcessId,
    DateTimeOffset? LastKnownProcessStartedAt,
    DateTimeOffset LastObservedAt,
    int? LastExitCode = null,
    string? LogPath = null,
    ResourceState? State = null,
    IReadOnlyList<ApplicationRuntimeVolumeMount>? VolumeMounts = null)
{
    public IReadOnlyList<ApplicationRuntimeVolumeMount> RuntimeVolumeMounts => VolumeMounts ?? [];
}

public sealed record ApplicationRuntimeVolumeMount(
    string VolumeReference,
    string TargetPath,
    string Source,
    bool ReadOnly,
    string Status = ApplicationRuntimeVolumeMountStatus.Materialized,
    string? Reason = null,
    DateTimeOffset? ObservedAt = null);

public static class ApplicationRuntimeVolumeMountStatus
{
    public const string Materialized = "materialized";
    public const string NotActive = "notActive";
}
