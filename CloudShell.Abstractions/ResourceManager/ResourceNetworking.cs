namespace CloudShell.Abstractions.ResourceManager;

public enum ResourceExposureScope
{
    Private,
    Local,
    Network,
    Public
}

public sealed record NetworkResourceDefinition(
    string Id,
    string Name,
    bool IsDefault = false);

public sealed record ServiceResourceDefinition(
    string Id,
    string Name,
    IReadOnlyList<ServiceTarget> Targets,
    IReadOnlyList<ServicePort> Ports,
    IReadOnlyList<string> NetworkIds,
    IReadOnlyList<ResourceHealthCheck>? HealthChecks = null)
{
    public IReadOnlyList<ResourceHealthCheck> ResourceHealthChecks => HealthChecks ?? [];
}

public sealed record ServiceTarget(
    string ResourceId,
    int Weight = 100);

public sealed record ServicePort(
    string Name,
    int TargetPort,
    int? Port = null,
    string Protocol = "tcp",
    ResourceExposureScope Exposure = ResourceExposureScope.Local);
