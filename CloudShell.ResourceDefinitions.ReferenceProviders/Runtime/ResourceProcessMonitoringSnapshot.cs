namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed record ResourceProcessMonitoringSnapshot(
    int ProcessId,
    DateTimeOffset? StartedAt,
    DateTimeOffset Timestamp,
    double CpuUsagePercent,
    TimeSpan TotalProcessorTime,
    long WorkingSetBytes,
    long PrivateMemoryBytes,
    int ThreadCount);
