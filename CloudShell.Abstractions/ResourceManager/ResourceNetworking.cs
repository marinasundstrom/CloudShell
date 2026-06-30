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
    string EndpointName)
{
    public static ResourceEndpointReference ForEndpoint(
        string resourceId,
        string endpointName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointName);

        return new ResourceEndpointReference(resourceId.Trim(), endpointName.Trim());
    }
}

public sealed record ResourceEndpointNetworkMapping(
    string Id,
    string Name,
    ResourceEndpointReference Target,
    string Address,
    ResourceExposureScope Exposure = ResourceExposureScope.Local,
    string? NetworkResourceId = null,
    string? ProviderResourceId = null,
    string? SourceEndpointName = null)
{
    public bool MatchesEndpoint(string endpointName)
    {
        if (string.IsNullOrWhiteSpace(endpointName))
        {
            return false;
        }

        var normalized = endpointName.Trim();
        return string.Equals(Target.EndpointName, normalized, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(SourceEndpointName, normalized, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Name, normalized, StringComparison.OrdinalIgnoreCase);
    }

    public bool TryGetUri(out Uri uri) =>
        ResourceEndpoint.TryGetUri(Address, out uri);

    public bool TryGetPort(out int port) =>
        ResourceEndpoint.TryGetPort(Address, out port);

    public static ResourceEndpointNetworkMapping ForEndpoint(
        string resourceId,
        string endpointName,
        string address,
        ResourceExposureScope exposure = ResourceExposureScope.Local,
        string? networkResourceId = null,
        string? providerResourceId = null,
        string? sourceEndpointName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointName);
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        var normalizedResourceId = resourceId.Trim();
        var normalizedEndpointName = endpointName.Trim();

        return new ResourceEndpointNetworkMapping(
            $"{normalizedResourceId}:endpoint-network-mapping:{normalizedEndpointName}",
            normalizedEndpointName,
            ResourceEndpointReference.ForEndpoint(normalizedResourceId, normalizedEndpointName),
            address.Trim(),
            exposure,
            NormalizeNullable(networkResourceId),
            NormalizeNullable(providerResourceId),
            NormalizeNullable(sourceEndpointName) ?? normalizedEndpointName);
    }

    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

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
    IResourceManagerStore ResourceManager,
    ResourceEndpointNetworkMapping? SourceEndpointNetworkMapping = null,
    ResourceEndpointNetworkMapping? TargetEndpointNetworkMapping = null);

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
    ResourceExposureScope Exposure = ResourceExposureScope.Local,
    ResourceEndpointAssignment Assignment = ResourceEndpointAssignment.ProviderDefault,
    string? NetworkResourceId = null,
    string? Host = null,
    string? IPAddress = null,
    string? ProviderEndpointId = null,
    ResourceOrchestratorSessionAffinityPolicy? SessionAffinity = null);

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
    IReadOnlyList<LoadBalancerRoute>? Routes = null,
    ResourceState? RuntimeState = null)
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

public sealed record LoadBalancerBackendTarget(
    string Host,
    int Port,
    string Protocol = "tcp",
    int Weight = 100);

public sealed record LoadBalancerRouteResolution(
    LoadBalancerRoute Route,
    Resource TargetResource,
    ResourceEndpoint? TargetEndpoint,
    IReadOnlyList<LoadBalancerBackendTarget>? Backends = null,
    ResourceEndpointNetworkMapping? TargetEndpointNetworkMapping = null)
{
    public IReadOnlyList<LoadBalancerBackendTarget> ResolvedBackends => Backends ?? [];
}

public sealed record LoadBalancerProviderContext(
    Resource LoadBalancerResource,
    LoadBalancerResourceDefinition Definition,
    Resource? HostResource,
    IReadOnlyList<LoadBalancerRouteResolution> Routes,
    IResourceManagerStore ResourceManager);

public sealed record DnsZoneResourceDefinition(
    string Id,
    string Name,
    string ZoneName,
    string? Provider = null,
    IReadOnlyList<DnsNameMappingDefinition>? Mappings = null)
{
    public IReadOnlyList<DnsNameMappingDefinition> DnsNameMappings => Mappings ?? [];
}

public sealed record DnsNameMappingDefinition(
    string Id,
    string Name,
    string HostName,
    string TargetResourceId,
    string? TargetEndpointName = null,
    ResourceExposureScope Exposure = ResourceExposureScope.Public,
    string? ProviderResourceId = null);

public sealed record DnsNameMappingResourceDefinition(
    string ZoneResourceId,
    string Id,
    string Name,
    string HostName,
    string TargetResourceId,
    string? TargetEndpointName = null,
    ResourceExposureScope Exposure = ResourceExposureScope.Public,
    string? ProviderResourceId = null);

public sealed record DnsNameMappingResolution(
    DnsNameMappingDefinition Mapping,
    Resource TargetResource,
    ResourceEndpoint? TargetEndpoint,
    ResourceEndpointNetworkMapping? TargetEndpointNetworkMapping = null,
    Resource? PublisherResource = null);

public sealed record DnsNamePublishingContext(
    Resource DnsZoneResource,
    DnsZoneResourceDefinition Definition,
    IReadOnlyList<DnsNameMappingResolution> Mappings,
    IReadOnlyList<Resource> PublisherResources,
    IResourceManagerStore ResourceManager);

public interface INamePublishingProvider
{
    string ProviderName { get; }

    bool CanPublish(DnsNamePublishingContext context);

    Task<ResourceProcedureResult> ReconcileAsync(
        DnsNamePublishingContext context,
        CancellationToken cancellationToken = default);
}

public interface INamePublishingActionAvailabilityProvider
{
    string? GetUnavailableReason(DnsNamePublishingContext context);
}

public interface ILoadBalancerProvider
{
    string ProviderName { get; }

    bool CanApply(LoadBalancerProviderContext context);

    Task<ResourceProcedureResult> ApplyAsync(
        LoadBalancerProviderContext context,
        CancellationToken cancellationToken = default);
}

public interface ILoadBalancerRuntimeProvider : ILoadBalancerProvider
{
    bool CanManageRuntime(LoadBalancerResourceDefinition definition);

    Task<ResourceProcedureResult> StartAsync(
        LoadBalancerProviderContext context,
        CancellationToken cancellationToken = default);

    Task<ResourceProcedureResult> StopAsync(
        LoadBalancerProviderContext context,
        CancellationToken cancellationToken = default);

    Task<ResourceProcedureResult> DeleteAsync(
        LoadBalancerProviderContext context,
        CancellationToken cancellationToken = default);
}
