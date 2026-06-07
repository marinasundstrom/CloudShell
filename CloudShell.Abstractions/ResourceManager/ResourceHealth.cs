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
