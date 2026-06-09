namespace CloudShell.Abstractions.ResourceManager;

public sealed record Resource(
    string Id,
    string Name,
    string Kind,
    string Provider,
    string Region,
    ResourceState State,
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
    IReadOnlyList<ResourceCapability>? Capabilities = null)
{
    private static readonly IReadOnlyDictionary<string, string> EmptyAttributes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public string PrimaryEndpoint => Endpoints.FirstOrDefault()?.Address ?? "none";

    public string EffectiveTypeId => TypeId ?? Kind;

    public IReadOnlyDictionary<string, string> ResourceAttributes => Attributes ?? EmptyAttributes;

    public IReadOnlyList<ResourceAction> ResourceActions => Actions ?? [];

    public ResourceAction? GetAction(string actionId) =>
        ResourceActions.FirstOrDefault(action =>
            string.Equals(action.Id, actionId, StringComparison.OrdinalIgnoreCase));

    public bool HasAction(string actionId) => GetAction(actionId) is not null;

    public ResourceAction? RunAction => GetAction(ResourceActionIds.Run);

    public ResourceAction? StopAction => GetAction(ResourceActionIds.Stop);

    public ResourceAction? PauseAction => GetAction(ResourceActionIds.Pause);

    public ResourceAction? RestartAction => GetAction(ResourceActionIds.Restart);

    public IReadOnlyList<ResourceHealthCheck> ResourceHealthChecks => HealthChecks ?? [];

    public ResourceObservability EffectiveObservability => Observability ?? ResourceObservability.None;

    public IReadOnlyList<ResourceCapability> ResourceCapabilities => Capabilities ?? [];

    public bool HasCapability(string capabilityId) =>
        ResourceCapabilities.Any(capability =>
            string.Equals(capability.Id, capabilityId, StringComparison.OrdinalIgnoreCase));
}

public enum ResourceClass
{
    Generic,
    Executable,
    Project,
    Container,
    Service,
    Network,
    Configuration,
    Infrastructure
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
    public const string ContainerEngineId = "container.engineId";
    public const string ContainerReplicas = "container.replicas";
    public const string ContainerRevision = "container.revision";
    public const string EndpointCount = "endpoints.count";
    public const string ConfigurationEntryCount = "configuration.entries";
    public const string InfrastructureKind = "infrastructure.kind";
    public const string NetworkKind = "network.kind";
    public const string ServiceTargetCount = "service.targets";
    public const string ServicePortCount = "service.ports";
}

public enum ResourceState
{
    Running,
    Starting,
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
    public const string NetworkingPolicy = "networking.policy";
    public const string NetworkingEgress = "networking.egress";
    public const string NetworkingTls = "networking.tls";
}
