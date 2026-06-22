using System.Text.Json.Serialization;

namespace CloudShell.Abstractions.ResourceManager;

public enum ResourceProbeType
{
    Health,
    Liveness,
    Readiness,
    Startup
}

public static class ResourceProbeSourceKinds
{
    public const string Http = "http";
}

public sealed record ResourceProbeSource(
    string Kind,
    ResourceHttpProbeSource? Http = null,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public static ResourceProbeSource ForHttp(
        string path,
        string? endpointName = null,
        TimeSpan? timeout = null) =>
        new(
            ResourceProbeSourceKinds.Http,
            new ResourceHttpProbeSource(path, endpointName, timeout));

    public bool IsHttp =>
        string.Equals(Kind, ResourceProbeSourceKinds.Http, StringComparison.OrdinalIgnoreCase) &&
        Http is not null;
}

public sealed record ResourceHttpProbeSource(
    string Path,
    string? EndpointName = null,
    TimeSpan? Timeout = null);

public sealed record ResourceHealthCheck(
    string Path,
    ResourceProbeType Type = ResourceProbeType.Health,
    string? EndpointName = null,
    string Name = "health",
    TimeSpan? Timeout = null,
    ResourceProbeSource? Source = null,
    int? IntervalSeconds = null)
{
    public ResourceHealthCheck(
        ResourceProbeSource source,
        ResourceProbeType type = ResourceProbeType.Health,
        string name = "health",
        int? intervalSeconds = null)
        : this(
            source.Http?.Path ?? string.Empty,
            type,
            source.Http?.EndpointName,
            name,
            source.Http?.Timeout,
            source,
            intervalSeconds)
    {
    }

    public ResourceProbeSource EffectiveSource =>
        Source ?? ResourceProbeSource.ForHttp(Path, EndpointName, Timeout);

    public ResourceHttpProbeSource? HttpSource =>
        EffectiveSource.IsHttp ? EffectiveSource.Http : null;
}

public interface IResourceProbeEvaluator
{
    bool CanEvaluate(Resource resource, ResourceHealthCheck check);

    Task<ResourceHealthCheckResult> EvaluateAsync(
        Resource resource,
        ResourceHealthCheck check,
        CancellationToken cancellationToken = default);
}

public enum ResourceHealthStatus
{
    Healthy,
    Unhealthy,
    Unknown
}

public enum ResourceHealthCheckOutcome
{
    Unknown,
    Responded,
    NoResponse,
    Unresolved,
    Unsupported
}

public static class ResourceHealthScopeKinds
{
    public const string Resource = "resource";

    public const string ResourceSet = "resourceSet";

    public const string Dependency = "dependency";

    public const string Service = "service";

    public const string Route = "route";

    public const string Runtime = "runtime";
}

public sealed record ResourceHealthSummary(
    string ResourceId,
    ResourceHealthStatus Status,
    DateTimeOffset CheckedAt,
    IReadOnlyList<ResourceHealthCheckResult> Checks);

public sealed record ResourceHealthScopeObservation(
    string ScopeId,
    string ScopeKind,
    ResourceHealthStatus Status,
    string Detail,
    ResourceHealthCheckOutcome Outcome = ResourceHealthCheckOutcome.Responded,
    string? DisplayName = null,
    string? ResourceId = null,
    DateTimeOffset? CheckedAt = null,
    IReadOnlyDictionary<string, string>? Attributes = null)
{
    private static readonly IReadOnlyDictionary<string, string> EmptyAttributes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore]
    public IReadOnlyDictionary<string, string> ObservationAttributes =>
        Attributes ?? EmptyAttributes;
}

public sealed record ResourceHealthCheckResult(
    ResourceHealthCheck Check,
    ResourceHealthStatus Status,
    string Detail,
    Uri? Uri,
    ResourceHealthCheckOutcome Outcome = ResourceHealthCheckOutcome.Responded,
    DateTimeOffset? CheckedAt = null,
    IReadOnlyList<ResourceHealthScopeObservation>? Observations = null)
{
    [JsonIgnore]
    public IReadOnlyList<ResourceHealthScopeObservation> ScopeObservations =>
        Observations ?? [];
}

public sealed class ResourceHealthOptions
{
    public const string SectionName = "ResourceManager:Health";

    public string SnapshotStore { get; set; } = ResourceHealthSnapshotStores.InMemory;

    public int RetainedSnapshotsPerResource { get; set; } = 50;
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
