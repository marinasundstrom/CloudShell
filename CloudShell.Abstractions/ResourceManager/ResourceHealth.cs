namespace CloudShell.Abstractions.ResourceManager;

public enum ResourceProbeType
{
    Health,
    Liveness,
    Readiness,
    Startup
}

public sealed record ResourceHealthCheck(
    string Path,
    ResourceProbeType Type = ResourceProbeType.Health,
    string? EndpointName = null,
    string Name = "health",
    TimeSpan? Timeout = null);

public enum ResourceHealthStatus
{
    Healthy,
    Unhealthy,
    Unknown
}

public sealed record ResourceHealthSummary(
    string ResourceId,
    ResourceHealthStatus Status,
    DateTimeOffset CheckedAt,
    IReadOnlyList<ResourceHealthCheckResult> Checks);

public sealed record ResourceHealthCheckResult(
    ResourceHealthCheck Check,
    ResourceHealthStatus Status,
    string Detail,
    Uri? Uri);

public sealed class ResourceHealthOptions
{
    public const string SectionName = "ResourceManager:Health";

    public string SnapshotStore { get; set; } = ResourceHealthSnapshotStores.InMemory;

    public int RetainedSnapshotsPerResource { get; set; }
}

public static class ResourceHealthSnapshotStores
{
    public const string InMemory = "InMemory";

    public const string Database = "Database";
}

public interface IResourceHealthStore
{
    IReadOnlyDictionary<string, ResourceHealthSummary> GetLatest(
        IEnumerable<string>? resourceIds = null);

    ResourceHealthSummary? GetLatest(string resourceId);

    IReadOnlyList<ResourceHealthSummary> GetSnapshots(
        string resourceId,
        int maxSnapshots = 100);

    void Add(ResourceHealthSummary summary);

    void AddRange(IEnumerable<ResourceHealthSummary> summaries);
}
