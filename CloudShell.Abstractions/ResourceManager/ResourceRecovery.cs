namespace CloudShell.Abstractions.ResourceManager;

public sealed record ResourceRecoveryPolicy(
    bool Enabled = false,
    string? ProbeName = null,
    ResourceProbeType ProbeType = ResourceProbeType.Liveness,
    int FailureThreshold = 3,
    int StartupGracePeriodSeconds = 30,
    int InitialBackoffSeconds = 5,
    int MaxBackoffSeconds = 300,
    double BackoffMultiplier = 2,
    int MaxAttempts = 10,
    int ResetAfterHealthySeconds = 300)
{
    public static ResourceRecoveryPolicy Disabled { get; } = new();
}

public enum ResourceRecoveryState
{
    Disabled,
    WaitingForSignal,
    Healthy,
    Failing,
    Scheduled,
    Restarting,
    Exhausted,
    Unavailable
}

public sealed record ResourceRecoveryStatus(
    string ResourceId,
    ResourceRecoveryPolicy Policy,
    ResourceRecoveryState State,
    int ConsecutiveFailures = 0,
    int AttemptCount = 0,
    DateTimeOffset? LastCheckedAt = null,
    DateTimeOffset? LastAttemptAt = null,
    DateTimeOffset? NextAttemptAt = null,
    string? LastDetail = null);

public sealed class ResourceRecoveryOptions
{
    public const string SectionName = "ResourceManager:Recovery";

    public bool EnableLocalPolling { get; set; }

    public int? PollIntervalSeconds { get; set; }
}
