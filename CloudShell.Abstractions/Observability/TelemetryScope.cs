namespace CloudShell.Abstractions.Observability;

public static class TelemetryAttributeNames
{
    public const string ScopeResourceId = "telemetry.scope.resourceId";
    public const string ScopeName = "telemetry.scope.name";
    public const string ScopeKind = "telemetry.scope.kind";
    public const string RuntimeReplicaOrdinal = "runtime.replica.ordinal";
    public const string RuntimeReplicaCount = "runtime.replica.count";
    public const string RuntimeContainerName = "runtime.container.name";
    public const string DeploymentRevision = "deployment.revision";
}

[Flags]
public enum TelemetrySignalKind
{
    None = 0,
    Logs = 1,
    Traces = 2,
    Metrics = 4
}

public enum TelemetrySourceKind
{
    Provider,
    Exporter,
    Endpoint
}

public sealed record TelemetryScope(
    string? ScopeResourceId = null,
    string? ScopeName = null,
    string? ScopeKind = null,
    string? DeploymentRevision = null)
{
    public bool HasAnyFilter =>
        !string.IsNullOrWhiteSpace(ScopeResourceId) ||
        !string.IsNullOrWhiteSpace(ScopeName) ||
        !string.IsNullOrWhiteSpace(ScopeKind) ||
        !string.IsNullOrWhiteSpace(DeploymentRevision);

    public bool Matches(IReadOnlyDictionary<string, string> attributes) =>
        MatchesAttribute(attributes, TelemetryAttributeNames.ScopeResourceId, ScopeResourceId) &&
        MatchesAttribute(attributes, TelemetryAttributeNames.ScopeName, ScopeName) &&
        MatchesAttribute(attributes, TelemetryAttributeNames.ScopeKind, ScopeKind) &&
        MatchesAttribute(attributes, TelemetryAttributeNames.DeploymentRevision, DeploymentRevision);

    private static bool MatchesAttribute(
        IReadOnlyDictionary<string, string> attributes,
        string key,
        string? expected) =>
        string.IsNullOrWhiteSpace(expected) ||
        (attributes.TryGetValue(key, out var actual) &&
            string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase));
}

public sealed record TelemetryScopeDescriptor(
    string ScopeResourceId,
    string Name,
    string Kind,
    string? Description = null,
    string? DeploymentRevision = null,
    IReadOnlyDictionary<string, string>? Attributes = null)
{
    public IReadOnlyDictionary<string, string> ScopeAttributes =>
        Attributes ?? EmptyAttributes;

    public TelemetryScope ToQueryScope() =>
        new(ScopeResourceId, Name, Kind, DeploymentRevision);

    private static readonly IReadOnlyDictionary<string, string> EmptyAttributes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed record TelemetrySourceDescriptor(
    string Id,
    string Name,
    TelemetrySignalKind Signals,
    TelemetrySourceKind Kind,
    string? Endpoint = null,
    string? Protocol = null,
    string? Description = null,
    IReadOnlyList<TelemetryScopeDescriptor>? Scopes = null,
    IReadOnlyDictionary<string, string>? Attributes = null)
{
    public IReadOnlyList<TelemetryScopeDescriptor> SourceScopes => Scopes ?? [];

    public IReadOnlyDictionary<string, string> SourceAttributes =>
        Attributes ?? EmptyAttributes;

    private static readonly IReadOnlyDictionary<string, string> EmptyAttributes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
