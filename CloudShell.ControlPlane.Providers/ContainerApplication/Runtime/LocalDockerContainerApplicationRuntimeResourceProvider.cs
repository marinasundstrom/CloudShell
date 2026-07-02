using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System.Globalization;
using GraphResource = CloudShell.ResourceModel.Resource;
using GraphResourceState = CloudShell.ResourceModel.ResourceState;
using ResourceManagerClass = CloudShell.Abstractions.ResourceManager.ResourceClass;
using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;
using ResourceManagerState = CloudShell.Abstractions.ResourceManager.ResourceState;

namespace CloudShell.ControlPlane.Providers;

public sealed class LocalDockerContainerApplicationRuntimeResourceProvider(
    ResourceGraphModel graph,
    ResourceResolver resolver,
    ILocalDockerContainerApplicationRuntimeBridge runtime,
    IConfiguration configuration,
    IOptions<LocalDockerContainerApplicationRuntimeOptions> options) : IResourceProvider
{
    private readonly LocalDockerContainerApplicationRuntimeOptions options = options.Value;

    public string Id => "local-docker-container-application.runtime";

    public string DisplayName => "Local Docker container application runtime";

    public IReadOnlyList<ResourceManagerResource> GetResources()
    {
        var snapshot = graph
            .GetSnapshotAsync()
            .AsTask()
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();
        return snapshot.Resources
            .Where(resource => options.Applications.ContainsKey(resource.EffectiveResourceId))
            .SelectMany(CreateRuntimeReplicaResources)
            .ToArray();
    }

    private IReadOnlyList<ResourceManagerResource> CreateRuntimeReplicaResources(
        GraphResourceState state)
    {
        var definition = options.Applications[state.EffectiveResourceId];
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
            .Select(replica => CreateRuntimeReplicaResource(definition, resource, parent, endpoint, replica, replicas))
            .ToArray();
    }

    private ResourceManagerResource CreateRuntimeReplicaResource(
        LocalDockerContainerApplicationRuntimeDefinition definition,
        GraphResource graphResource,
        ResourceManagerResource parent,
        NetworkingEndpointRequestValue endpoint,
        int replica,
        int replicaCount)
    {
        var resourceId = LocalDockerContainerApplicationRuntimeConventions.CreateReplicaResourceId(definition, replica);
        var containerName = LocalDockerContainerApplicationRuntimeConventions.CreateReplicaContainerName(definition, replica);
        var replicaOrdinal = replica.ToString(CultureInfo.InvariantCulture);
        var totalReplicas = replicaCount.ToString(CultureInfo.InvariantCulture);
        var replicaName = $"Replica {replicaOrdinal}";
        var deploymentServiceId = ResourceOrchestratorReplicaGroups.CreateDefaultServiceName(graphResource.EffectiveResourceId);
        var runtimeRevisionId = LocalDockerContainerApplicationRuntimeConventions.ResolveRuntimeRevisionId(graphResource);
        var replicaGroupId = ResourceOrchestratorReplicaGroups.CreateReplicaGroupId(
            deploymentServiceId,
            runtimeRevisionId);
        var protocol = NormalizeProtocol(endpoint.Protocol);
        var targetPort = endpoint.TargetPort ?? endpoint.Port ?? 8080;
        var probePort = LocalDockerContainerApplicationRuntimeConventions.ResolveReplicaProbePort(
            definition,
            configuration,
            replica,
            endpoint.Port ?? targetPort);

        return new ResourceManagerResource(
            resourceId,
            $"{parent.Name} replica {replica.ToString(CultureInfo.InvariantCulture)}",
            "runtime.container",
            definition.RuntimeResourceProviderName,
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
                [ResourceAttributeNames.DeploymentServiceId] = deploymentServiceId,
                [ResourceAttributeNames.DeploymentReplicaGroupId] = replicaGroupId,
                [ResourceAttributeNames.RuntimeKind] = "containerReplica",
                [ResourceAttributeNames.RuntimeContainerName] = containerName,
                [ResourceAttributeNames.RuntimeReplicaOrdinal] = replicaOrdinal,
                [ResourceAttributeNames.RuntimeReplicaCount] = totalReplicas,
                [ResourceAttributeNames.RuntimeRevision] = runtimeRevisionId,
                [ResourceAttributeNames.RuntimeMaterialization] = definition.RuntimeMaterialization
            },
            Capabilities:
            [
                new(ResourceCapabilityIds.LogSources),
                new(ResourceCapabilityIds.Monitoring)
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
                definition,
                parent.Id,
                resourceId,
                replicaName,
                containerName,
                replicaOrdinal,
                totalReplicas,
                runtimeRevisionId));
    }

    private static ResourceObservability CreateRuntimeReplicaObservability(
        LocalDockerContainerApplicationRuntimeDefinition definition,
        string parentResourceId,
        string replicaResourceId,
        string replicaName,
        string containerName,
        string replicaOrdinal,
        string replicaCount,
        string runtimeRevisionId) =>
        new(
            Logs: true,
            Traces: true,
            Metrics: true,
            ServiceName: $"{definition.ReplicaServiceNamePrefix}{replicaOrdinal}",
            ResourceAttributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["cloudshell.resource.id"] = replicaResourceId,
                ["cloudshell.resource.type"] = "runtime.container",
                [TelemetryAttributeNames.ScopeResourceId] = parentResourceId,
                [TelemetryAttributeNames.ScopeName] = replicaName,
                [TelemetryAttributeNames.ScopeKind] = "runtime",
                [TelemetryAttributeNames.RuntimeReplicaOrdinal] = replicaOrdinal,
                [TelemetryAttributeNames.RuntimeReplicaCount] = replicaCount,
                [TelemetryAttributeNames.RuntimeContainerName] = containerName,
                [TelemetryAttributeNames.DeploymentRevision] = runtimeRevisionId
            },
            Scopes:
            [
                new(
                    parentResourceId,
                    replicaName,
                    "runtime",
                    $"Runtime replica {replicaOrdinal}",
                    DeploymentRevision: runtimeRevisionId,
                    Attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [TelemetryAttributeNames.RuntimeReplicaOrdinal] = replicaOrdinal,
                        [TelemetryAttributeNames.RuntimeReplicaCount] = replicaCount,
                        [TelemetryAttributeNames.RuntimeContainerName] = containerName,
                        [TelemetryAttributeNames.DeploymentRevision] = runtimeRevisionId
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
