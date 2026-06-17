namespace CloudShell.Abstractions.ResourceManager;

public sealed record Resource(
    string Id,
    string Name,
    string Kind,
    string Provider,
    string Region,
    ResourceState? State,
    IReadOnlyList<ResourceEndpoint> Endpoints,
    string Version,
    DateTimeOffset LastUpdated,
    IReadOnlyList<string> DependsOn,
    string? DetailRoute = null,
    string? ParentResourceId = null,
    string? TypeId = null,
    IReadOnlyList<ResourceAction>? Actions = null,
    IReadOnlyList<ResourceHealthCheck>? HealthChecks = null,
    ResourceObservability? Observability = null,
    ResourceClass ResourceClass = ResourceClass.Generic,
    IReadOnlyDictionary<string, string>? Attributes = null,
    IReadOnlyList<ResourceCapability>? Capabilities = null,
    IReadOnlyList<ResourceEndpointMappingDefinition>? EndpointMappings = null,
    IReadOnlyList<ResourceEndpointNetworkMapping>? EndpointNetworkMappings = null,
    IReadOnlyList<LoadBalancerRoute>? LoadBalancerRoutes = null,
    ResourceIdentityBinding? Identity = null,
    ResourceSource Source = ResourceSource.User,
    ResourceManagementMode ManagementMode = ResourceManagementMode.UserManaged,
    ResourceVisibility Visibility = ResourceVisibility.Normal,
    string? OwnerResourceId = null,
    ResourceCleanupBehavior CleanupBehavior = ResourceCleanupBehavior.None,
    string? DisplayName = null)
{
    private static readonly IReadOnlyDictionary<string, string> EmptyAttributes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public string PrimaryEndpoint =>
        ResourceEndpointNetworkMappings.FirstOrDefault()?.Address ??
        "none";

    public string EffectiveTypeId => TypeId ?? Kind;

    public IReadOnlyDictionary<string, string> ResourceAttributes => Attributes ?? EmptyAttributes;

    public IReadOnlyList<ResourceAction> ResourceActions => Actions ?? [];

    public ResourceAction? GetAction(string actionId) =>
        ResourceActions.FirstOrDefault(action =>
            string.Equals(action.Id, actionId, StringComparison.OrdinalIgnoreCase));

    public bool HasAction(string actionId) => GetAction(actionId) is not null;

    public ResourceAction? StartAction => GetAction(ResourceActionIds.Start);

    public ResourceAction? StopAction => GetAction(ResourceActionIds.Stop);

    public ResourceAction? PauseAction => GetAction(ResourceActionIds.Pause);

    public ResourceAction? RestartAction => GetAction(ResourceActionIds.Restart);

    public IReadOnlyList<ResourceHealthCheck> ResourceHealthChecks => HealthChecks ?? [];

    public ResourceObservability EffectiveObservability => Observability ?? ResourceObservability.None;

    public IReadOnlyList<ResourceCapability> ResourceCapabilities => Capabilities ?? [];

    public IReadOnlyList<ResourceEndpointMappingDefinition> ResourceEndpointMappings =>
        EndpointMappings ?? [];

    public IReadOnlyList<ResourceEndpointNetworkMapping> ResourceEndpointNetworkMappings =>
        EndpointNetworkMappings ?? [];

    public ResourceEndpointNetworkMapping? GetEndpointNetworkMapping(string endpointName) =>
        ResourceEndpointNetworkMappings.FirstOrDefault(mapping => mapping.MatchesEndpoint(endpointName));

    public string? GetEndpointNetworkAddress(string endpointName) =>
        GetEndpointNetworkMapping(endpointName)?.Address;

    public string? GetResolvedEndpointAddress(string endpointName)
    {
        if (string.IsNullOrWhiteSpace(endpointName))
        {
            return null;
        }

        var normalized = endpointName.Trim();
        return GetEndpointNetworkAddress(normalized);
    }

    public string? GetResolvedEndpointAddress(ResourceEndpoint endpoint) =>
        GetEndpointNetworkAddress(endpoint.Name);

    public bool TryGetResolvedEndpointUri(string endpointName, out Uri uri)
    {
        if (string.IsNullOrWhiteSpace(endpointName))
        {
            uri = null!;
            return false;
        }

        var normalized = endpointName.Trim();
        if (GetEndpointNetworkMapping(normalized) is { } mapping &&
            mapping.TryGetUri(out uri))
        {
            return true;
        }

        uri = null!;
        return false;
    }

    public bool TryGetResolvedEndpointUri(ResourceEndpoint endpoint, out Uri uri)
    {
        if (GetEndpointNetworkMapping(endpoint.Name) is { } mapping &&
            mapping.TryGetUri(out uri))
        {
            return true;
        }

        uri = null!;
        return false;
    }

    public IReadOnlyList<LoadBalancerRoute> ResourceLoadBalancerRoutes =>
        LoadBalancerRoutes ?? [];

    public ResourceIdentityBinding? IdentityBinding => Identity;

    public bool IsNormalResource => Visibility == ResourceVisibility.Normal;

    public bool IsRuntimeManaged =>
        ManagementMode == ResourceManagementMode.RuntimeManaged ||
        ManagementMode == ResourceManagementMode.OrchestratorManaged;

    public string EffectiveDisplayName =>
        string.IsNullOrWhiteSpace(DisplayName) ? Name : DisplayName;

    public bool HasCapability(string capabilityId) =>
        ResourceCapabilities.Any(capability =>
            string.Equals(capability.Id, capabilityId, StringComparison.OrdinalIgnoreCase));

}

public enum ResourceSource
{
    User,
    Provider,
    Orchestrator,
    RuntimeController
}

public enum ResourceManagementMode
{
    UserManaged,
    ProviderManaged,
    OrchestratorManaged,
    RuntimeManaged
}

public enum ResourceVisibility
{
    Normal,
    Hidden,
    Diagnostic
}

public enum ResourceCleanupBehavior
{
    None,
    DeleteWithOwner,
    DetachWithOwner
}

public enum ResourceClass
{
    Generic,
    Executable,
    Project,
    Container,
    Service,
    Network,
    Storage,
    Configuration,
    Infrastructure,
    SecretsVault
}

public static class ResourceAttributeNames
{
    public const string WorkloadKind = "workload.kind";
    public const string ExecutablePath = "executable.path";
    public const string ExecutableArguments = "executable.arguments";
    public const string WorkingDirectory = "executable.workingDirectory";
    public const string ProjectPath = "project.path";
    public const string ProjectArguments = "project.arguments";
    public const string ProjectHotReload = "project.hotReload";
    public const string ContainerImage = "container.image";
    public const string ContainerRegistry = "container.registry";
    public const string ContainerBuildContext = "container.buildContext";
    public const string ContainerDockerfile = "container.dockerfile";
    public const string ContainerHostId = "container.engineId";
    public const string ContainerReplicas = "container.replicas";
    public const string ContainerReplicasEnabled = "container.replicas.enabled";
    public const string ContainerRevision = "container.revision";
    public const string DeploymentId = "deployment.id";
    public const string DeploymentServiceId = "deployment.serviceId";
    public const string DeploymentStatus = "deployment.status";
    public const string DeploymentRevision = "deployment.revision";
    public const string DeploymentWorkloadVersion = "deployment.workloadVersion";
    public const string DeploymentDesiredReplicas = "deployment.replicas.desired";
    public const string DeploymentProjectedReplicas = "deployment.replicas.projected";
    public const string RuntimeKind = "runtime.kind";
    public const string RuntimeContainerName = "runtime.container.name";
    public const string RuntimeReplicaOrdinal = "runtime.replica.ordinal";
    public const string RuntimeReplicaCount = "runtime.replica.count";
    public const string RuntimeRevision = "runtime.revision";
    public const string RuntimeMaterialization = "runtime.materialization";
    public const string VolumeProvider = "storage.volume.provider";
    public const string VolumeStorageMedium = "storage.volume.medium";
    public const string VolumeLocation = "storage.volume.location";
    public const string VolumeStorageResourceId = "storage.volume.storageResourceId";
    public const string VolumeSubPath = "storage.volume.subPath";
    public const string VolumeAccessMode = "storage.volume.accessMode";
    public const string VolumePersistent = "storage.volume.persistent";
    public const string VolumeMountCount = "storage.volumeMounts";
    public const string VolumeMountMaterializedCount = "storage.volumeMounts.materialized";
    public const string VolumeMountMaterializationStatus = "storage.volumeMounts.materializationStatus";
    public const string StorageProvider = "storage.provider";
    public const string StorageMedium = "storage.medium";
    public const string StorageLocation = "storage.location";
    public const string StorageVolumeCount = "storage.volumes";
    public const string StorageRuntimeStatus = "storage.runtimeStatus";
    public const string StorageRuntimeStatusReason = "storage.runtimeStatusReason";
    public const string EndpointCount = "endpoints.count";
    public const string ConfigurationEntryCount = "configuration.entries";
    public const string InfrastructureKind = "infrastructure.kind";
    public const string NetworkKind = "network.kind";
    public const string NetworkHostReadiness = "network.hostReadiness";
    public const string NetworkMappingProviders = "network.mappingProviders";
    public const string NetworkProvisionedMappingCount = "network.provisionedMappings";
    public const string DnsZoneName = "dns.zone";
    public const string DnsProvider = "dns.provider";
    public const string DnsRecordCount = "dns.records";
    public const string DnsConflictCount = "dns.conflicts";
    public const string NameMappingHostName = "nameMapping.hostName";
    public const string NameMappingTargetResourceId = "nameMapping.targetResourceId";
    public const string NameMappingTargetEndpointName = "nameMapping.targetEndpointName";
    public const string NameMappingExposure = "nameMapping.exposure";
    public const string NameMappingProviderResourceId = "nameMapping.providerResourceId";
    public const string NameMappingStatus = "nameMapping.status";
    public const string NameMappingStatusReason = "nameMapping.statusReason";
    public const string NameMappingMaterializationStatus = "nameMapping.materializationStatus";
    public const string NameMappingMaterializationStatusReason = "nameMapping.materializationStatusReason";
    public const string LoadBalancerProvider = "loadBalancer.provider";
    public const string LoadBalancerHostResourceId = "loadBalancer.hostResourceId";
    public const string LoadBalancerEntrypointCount = "loadBalancer.entrypoints";
    public const string LoadBalancerRouteCount = "loadBalancer.routes";
    public const string LoadBalancerHttpRouteCount = "loadBalancer.routes.http";
    public const string LoadBalancerTcpRouteCount = "loadBalancer.routes.tcp";
    public const string ServiceTargetCount = "service.targets";
    public const string ServicePortCount = "service.ports";
}

public enum ResourceState
{
    Running,
    Starting,
    Stopping,
    Paused,
    Degraded,
    Stopped,
    Unknown
}

public sealed record ResourceCapability(
    string Id,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    private static readonly IReadOnlyDictionary<string, string> EmptyMetadata =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> CapabilityMetadata => Metadata ?? EmptyMetadata;
}

public static class ResourceCapabilityIds
{
    public const string EndpointSource = "endpoint.source";
    public const string ContainerHost = "container.host";
    public const string EnvironmentVariables = "environment.variables";
    public const string NetworkingProvider = "networking.provider";
    public const string NetworkingEndpointProvider = "networking.endpointProvider";
    public const string NetworkingEndpointMapper = "networking.endpointMapper";
    public const string NetworkingHostNetwork = "networking.hostNetwork";
    public const string NetworkingVirtualNetwork = "networking.virtualNetwork";
    public const string NetworkingIngress = "networking.ingress";
    public const string NetworkingGateway = "networking.gateway";
    public const string NetworkingLoadBalancer = "networking.loadBalancer";
    public const string NetworkingBackendPool = "networking.backendPool";
    public const string NetworkingCluster = "networking.cluster";
    public const string NetworkingClusterNode = "networking.clusterNode";
    public const string NetworkingHealthProbe = "networking.healthProbe";
    public const string NetworkingTrafficSplit = "networking.trafficSplit";
    public const string NetworkingServiceDiscovery = "networking.serviceDiscovery";
    public const string NetworkingDnsZone = "networking.dnsZone";
    public const string NetworkingNameMapping = "networking.nameMapping";
    public const string NetworkingNamePublisher = "networking.namePublisher";
    public const string NetworkingNameResolver = "networking.nameResolver";
    public const string NetworkingPolicy = "networking.policy";
    public const string NetworkingEgress = "networking.egress";
    public const string NetworkingTls = "networking.tls";
    public const string StorageVolume = "storage.volume";
    public const string StorageProvider = "storage.provider";
    public const string StorageVolumeConsumer = "storage.volumeConsumer";
    public const string StorageMountProvider = "storage.mountProvider";
}
