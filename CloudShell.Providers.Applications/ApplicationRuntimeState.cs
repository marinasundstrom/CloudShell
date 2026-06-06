namespace CloudShell.Providers.Applications;

public sealed record ApplicationRuntimeState(
    string ApplicationId,
    int? LastKnownProcessId,
    DateTimeOffset? LastKnownProcessStartedAt,
    DateTimeOffset LastObservedAt,
    int? LastExitCode = null,
    string? LogPath = null);
