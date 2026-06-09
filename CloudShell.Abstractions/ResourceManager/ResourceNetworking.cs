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

public enum NetworkResourceKind
{
    Logical,
    Virtual,
    Host
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

public sealed record ResourceEndpointMappingProvisioningContext(
    Resource NetworkResource,
    NetworkResourceDefinition NetworkDefinition,
    ResourceEndpointMappingDefinition Mapping,
    ResourceEndpoint SourceEndpoint,
    Resource TargetResource,
    ResourceEndpoint TargetEndpoint,
    Resource ProviderResource,
    IResourceManagerStore ResourceManager);

public interface IResourceEndpointMappingProvisioner
{
    bool CanProvisionEndpointMapping(ResourceEndpointMappingProvisioningContext context);

    Task<ResourceProcedureResult> ProvisionEndpointMappingAsync(
        ResourceEndpointMappingProvisioningContext context,
        CancellationToken cancellationToken = default);
}

public sealed record NetworkResourceDefinition(
    string Id,
    string Name,
    bool IsDefault = false,
    IReadOnlyList<ResourceEndpointRequest>? Endpoints = null,
    IReadOnlyList<ResourceEndpointMappingDefinition>? EndpointMappings = null,
    NetworkResourceKind Kind = NetworkResourceKind.Logical)
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

public enum LoadBalancerRouteKind
{
    Http,
    Tcp
}

public sealed record LoadBalancerResourceDefinition(
    string Id,
    string Name,
    string Provider,
    string? HostResourceId = null,
    IReadOnlyList<LoadBalancerEntrypoint>? Entrypoints = null,
    IReadOnlyList<LoadBalancerRoute>? Routes = null)
{
    public IReadOnlyList<LoadBalancerEntrypoint> LoadBalancerEntrypoints =>
        Entrypoints ?? [];

    public IReadOnlyList<LoadBalancerRoute> LoadBalancerRoutes =>
        Routes ?? [];
}

public sealed record LoadBalancerEntrypoint(
    string Name,
    ResourceEndpointProtocol Protocol,
    int Port,
    ResourceExposureScope Exposure = ResourceExposureScope.Public);

public sealed record LoadBalancerRoute(
    string Id,
    string Name,
    LoadBalancerRouteKind Kind,
    string EntrypointName,
    LoadBalancerRouteMatch Match,
    LoadBalancerRouteTarget Target);

public sealed record LoadBalancerRouteMatch(
    string? Host = null,
    string? PathPrefix = null,
    int? Port = null);

public sealed record LoadBalancerRouteTarget(
    string ResourceId,
    string? EndpointName = null,
    int? Port = null);

public sealed record LoadBalancerRouteResolution(
    LoadBalancerRoute Route,
    Resource TargetResource,
    ResourceEndpoint? TargetEndpoint);

public sealed record LoadBalancerProviderContext(
    Resource LoadBalancerResource,
    LoadBalancerResourceDefinition Definition,
    Resource? HostResource,
    IReadOnlyList<LoadBalancerRouteResolution> Routes,
    IResourceManagerStore ResourceManager);

public interface ILoadBalancerProvider
{
    string ProviderName { get; }

    bool CanApply(LoadBalancerProviderContext context);

    Task<ResourceProcedureResult> ApplyAsync(
        LoadBalancerProviderContext context,
        CancellationToken cancellationToken = default);
}
