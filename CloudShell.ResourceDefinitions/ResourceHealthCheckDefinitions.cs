using System.Text.Json.Serialization;

namespace CloudShell.ResourceDefinitions;

public static class ResourceHealthCheckCapabilityIds
{
    public static readonly ResourceCapabilityId HealthChecks = "health.checks";
    public static readonly ResourceCapabilityId Liveness = "liveness";
}

public static class ResourceHealthCheckDefinitionValues
{
    public const string Health = "health";
    public const string Liveness = "liveness";
    public const string Readiness = "readiness";
    public const string Startup = "startup";
    public const string Http = "http";
}

public sealed record ResourceHealthCheckDefinitionSet(
    [property: JsonPropertyName("checks")]
    IReadOnlyList<ResourceHealthCheckDefinition>? Checks = null);

public sealed record ResourceHealthCheckDefinition(
    [property: JsonPropertyName("name")]
    string Name,
    [property: JsonPropertyName("type")]
    string Type,
    [property: JsonPropertyName("source")]
    ResourceProbeSourceDefinition Source,
    [property: JsonPropertyName("intervalSeconds")]
    int? IntervalSeconds = null)
{
    public static ResourceHealthCheckDefinition Http(
        string path,
        string? endpointName = null,
        string name = "health",
        int? timeoutMilliseconds = null,
        int? intervalSeconds = null) =>
        new(
            name,
            ResourceHealthCheckDefinitionValues.Health,
            ResourceProbeSourceDefinition.ForHttp(path, endpointName, timeoutMilliseconds),
            intervalSeconds);

    public static ResourceHealthCheckDefinition HttpLiveness(
        string path,
        string? endpointName = null,
        string name = "liveness",
        int? timeoutMilliseconds = null,
        int? intervalSeconds = null) =>
        new(
            name,
            ResourceHealthCheckDefinitionValues.Liveness,
            ResourceProbeSourceDefinition.ForHttp(path, endpointName, timeoutMilliseconds),
            intervalSeconds);
}

public sealed record ResourceProbeSourceDefinition(
    [property: JsonPropertyName("kind")]
    string Kind,
    [property: JsonPropertyName("http")]
    ResourceHttpProbeSourceDefinition? Http = null,
    [property: JsonPropertyName("metadata")]
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public static ResourceProbeSourceDefinition ForHttp(
        string path,
        string? endpointName = null,
        int? timeoutMilliseconds = null) =>
        new(
            ResourceHealthCheckDefinitionValues.Http,
            new ResourceHttpProbeSourceDefinition(path, endpointName, timeoutMilliseconds));
}

public sealed record ResourceHttpProbeSourceDefinition(
    [property: JsonPropertyName("path")]
    string Path,
    [property: JsonPropertyName("endpointName")]
    string? EndpointName = null,
    [property: JsonPropertyName("timeoutMilliseconds")]
    int? TimeoutMilliseconds = null);
