using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using CloudShell.ResourceDefinitions.ResourceManager;
using Microsoft.Extensions.Configuration;
using System.Globalization;
using GraphResource = CloudShell.ResourceDefinitions.Resource;
using ResourceManagerClass = CloudShell.Abstractions.ResourceManager.ResourceClass;
using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;
using ResourceManagerState = CloudShell.Abstractions.ResourceManager.ResourceState;

internal sealed class ReplicatedContainerHealthGraphOnlyRuntimeResourceProvider(
    ResourceGraphModel graph,
    ResourceResolver resolver,
    IReplicatedContainerHealthGraphContainerAppRuntimeBridge runtime,
    IConfiguration configuration) : IResourceProvider
{
    public string Id => "replicated-container-health.graph-only-runtime";

    public string DisplayName => "Replicated Container Health runtime";

    public IReadOnlyList<ResourceManagerResource> GetResources()
    {
        var snapshot = graph
            .GetSnapshotAsync()
            .AsTask()
            .GetAwaiter()
            .GetResult();
        var state = snapshot.Resources.FirstOrDefault(resource => string.Equals(
            resource.EffectiveResourceId,
            ReplicatedContainerHealthGraphOnlyRuntimeConventions.GraphApiResourceId,
            StringComparison.OrdinalIgnoreCase));
        if (state is null)
        {
            return [];
        }

        var resource = resolver.Resolve(state);
        if (runtime.GetStatus(resource) != ContainerApplicationRuntimeStatus.Running)
        {
            return [];
        }

        var parent = ResourceModelResourceManagerMapper.ToResourceManagerResource(resource);
        if (parent.ResourceHealthChecks.Count == 0)
        {
            return [];
        }

        var endpoint = ResolveHttpEndpoint(resource);
        if (endpoint is null)
        {
            return [];
        }

        var replicas = ResolveReplicas(resource);
        return Enumerable
            .Range(1, replicas)
            .Select(replica => CreateRuntimeReplicaResource(parent, endpoint, replica, replicas))
            .ToArray();
    }

    private ResourceManagerResource CreateRuntimeReplicaResource(
        ResourceManagerResource parent,
        NetworkingEndpointRequestValue endpoint,
        int replica,
        int replicaCount)
    {
        var resourceId = ReplicatedContainerHealthGraphOnlyRuntimeConventions.CreateReplicaResourceId(replica);
        var containerName = ReplicatedContainerHealthGraphOnlyRuntimeConventions.CreateReplicaContainerName(replica);
        var replicaOrdinal = replica.ToString(CultureInfo.InvariantCulture);
        var totalReplicas = replicaCount.ToString(CultureInfo.InvariantCulture);
        var replicaName = $"Replica {replicaOrdinal}";
        var protocol = NormalizeProtocol(endpoint.Protocol);
        var targetPort = endpoint.TargetPort ?? endpoint.Port ?? 8080;
        var probePort = ReplicatedContainerHealthGraphOnlyRuntimeConventions.ResolveReplicaProbePort(
            configuration,
            replica,
            endpoint.Port ?? targetPort);

        return new ResourceManagerResource(
            resourceId,
            $"graph-api replica {replica.ToString(CultureInfo.InvariantCulture)}",
            "runtime.container",
            DisplayName,
            "local",
            ResourceManagerState.Running,
            [ResourceEndpoint.Contract(endpoint.Name, protocol, ResourceExposureScope.Local, targetPort)],
            parent.Version,
            DateTimeOffset.UtcNow,
            [],
            ParentResourceId: parent.Id,
            TypeId: "runtime.container",
            HealthChecks: parent.ResourceHealthChecks,
            ResourceClass: ResourceManagerClass.Container,
            Attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ResourceAttributeNames.RuntimeKind] = "containerReplica",
                [ResourceAttributeNames.RuntimeContainerName] = containerName,
                [ResourceAttributeNames.RuntimeReplicaOrdinal] = replicaOrdinal,
                [ResourceAttributeNames.RuntimeReplicaCount] = totalReplicas,
                [ResourceAttributeNames.RuntimeMaterialization] = "sampleGraphOnlyRuntime"
            },
            Capabilities:
            [
                new(ResourceCapabilityIds.LogSources)
            ],
            EndpointNetworkMappings:
            [
                ResourceEndpointNetworkMapping.ForEndpoint(
                    resourceId,
                    endpoint.Name,
                    $"{protocol}://localhost:{probePort.ToString(CultureInfo.InvariantCulture)}",
                    ResourceExposureScope.Local,
                    sourceEndpointName: endpoint.Name)
            ],
            Source: ResourceSource.Orchestrator,
            ManagementMode: ResourceManagementMode.RuntimeManaged,
            Visibility: ResourceVisibility.Hidden,
            OwnerResourceId: parent.Id,
            CleanupBehavior: ResourceCleanupBehavior.DeleteWithOwner,
            Observability: CreateRuntimeReplicaObservability(
                parent.Id,
                resourceId,
                replicaName,
                containerName,
                replicaOrdinal,
                totalReplicas));
    }

    private static ResourceObservability CreateRuntimeReplicaObservability(
        string parentResourceId,
        string replicaResourceId,
        string replicaName,
        string containerName,
        string replicaOrdinal,
        string replicaCount) =>
        new(
            Logs: true,
            Traces: true,
            Metrics: true,
            ServiceName: $"replicated-container-health-graph-api-replica-{replicaOrdinal}",
            ResourceAttributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["cloudshell.resource.id"] = replicaResourceId,
                ["cloudshell.resource.type"] = "runtime.container",
                [TelemetryAttributeNames.ScopeResourceId] = parentResourceId,
                [TelemetryAttributeNames.ScopeName] = replicaName,
                [TelemetryAttributeNames.ScopeKind] = "runtime",
                [TelemetryAttributeNames.RuntimeReplicaOrdinal] = replicaOrdinal,
                [TelemetryAttributeNames.RuntimeReplicaCount] = replicaCount,
                [TelemetryAttributeNames.RuntimeContainerName] = containerName
            },
            Scopes:
            [
                new(
                    parentResourceId,
                    replicaName,
                    "runtime",
                    $"Runtime replica {replicaOrdinal}",
                    Attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [TelemetryAttributeNames.RuntimeReplicaOrdinal] = replicaOrdinal,
                        [TelemetryAttributeNames.RuntimeReplicaCount] = replicaCount,
                        [TelemetryAttributeNames.RuntimeContainerName] = containerName
                    })
            ]);

    private static NetworkingEndpointRequestValue? ResolveHttpEndpoint(GraphResource resource) =>
        resource.Attributes
            .GetObject<NetworkingEndpointRequestValue[]>(
                ContainerApplicationResourceTypeProvider.Attributes.EndpointRequests)?
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Name, "http", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate.Protocol, "http", StringComparison.OrdinalIgnoreCase));

    private static int ResolveReplicas(GraphResource resource) =>
        int.TryParse(
            resource.Attributes.GetString(ContainerApplicationResourceTypeProvider.Attributes.ContainerReplicas),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var replicas)
            ? Math.Max(1, replicas)
            : 1;

    private static string NormalizeProtocol(string? protocol) =>
        string.IsNullOrWhiteSpace(protocol) ? "http" : protocol.Trim().ToLowerInvariant();
}
