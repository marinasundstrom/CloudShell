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
