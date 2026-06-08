namespace CloudShell.Abstractions.ResourceManager;

public enum ResourceEndpointProtocol
{
    Http,
    Https,
    Tcp,
    Udp
}

public enum ResourceEndpointAssignment
{
    Manual,
    Auto,
    ProviderDefault,
    Predefined
}

public enum ResourceExposureScope
{
    Private,
    Local,
    Network,
    Public
}

public sealed record ResourceEndpointRequest(
    string Name,
    ResourceEndpointProtocol Protocol,
    int? TargetPort = null,
    string? Host = null,
    int? Port = null,
    string? IPAddress = null,
    ResourceExposureScope Exposure = ResourceExposureScope.Local,
    ResourceEndpointAssignment Assignment = ResourceEndpointAssignment.ProviderDefault,
    string? NetworkResourceId = null,
    string? ProviderEndpointId = null)
{
    public string ProtocolName => Protocol.ToString().ToLowerInvariant();
}

public sealed record ResourceEndpointReference(
    string ResourceId,
    string EndpointName);

public sealed record ResourceEndpointMappingDefinition(
    string Id,
    string Name,
    ResourceEndpointReference Source,
    ResourceEndpointReference Target,
    string? NetworkResourceId = null,
    string? ProviderResourceId = null);

public sealed record NetworkResourceDefinition(
    string Id,
    string Name,
    bool IsDefault = false,
    IReadOnlyList<ResourceEndpointRequest>? Endpoints = null,
    IReadOnlyList<ResourceEndpointMappingDefinition>? EndpointMappings = null)
{
    public IReadOnlyList<ResourceEndpointRequest> NetworkEndpoints => Endpoints ?? [];

    public IReadOnlyList<ResourceEndpointMappingDefinition> NetworkEndpointMappings =>
        EndpointMappings ?? [];
}

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
