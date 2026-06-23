using CloudShell.Abstractions.ResourceManager;
using System.Globalization;

namespace CloudShell.Providers.Applications;

public sealed partial class ApplicationResourceService
{
    private IReadOnlyList<Resource> CreateRuntimeContainerResources(ApplicationResourceDefinition application)
    {
        if (!IsReplicaModeEnabled(application))
        {
            return [];
        }

        var parentState = GetState(application.Id);
        var deployment = CreateDefaultContainerOrchestratorDeployment(
            application,
            parentState,
            runtimeRevisionScoped: true);
        var replicaGroup = CreateDefaultContainerReplicaGroup(deployment.Spec.Service);
        return replicaGroup.Instances
            .Select(instance => CreateRuntimeContainerResource(application, deployment, replicaGroup, instance, parentState))
            .ToArray();
    }

    private Resource CreateRuntimeContainerResource(
        ApplicationResourceDefinition application,
        ResourceOrchestratorDeployment deployment,
        ResourceOrchestratorReplicaGroup replicaGroup,
        ResourceOrchestratorServiceInstance instance,
        ResourceState state)
    {
        var service = deployment.Spec.Service;
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ResourceAttributeNames.DeploymentId] = deployment.Id,
            [ResourceAttributeNames.DeploymentServiceId] = deployment.ServiceId,
            [ResourceAttributeNames.DeploymentRevision] = deployment.RevisionId,
            [ResourceAttributeNames.DeploymentReplicaGroupId] = replicaGroup.Id,
            [ResourceAttributeNames.RuntimeKind] = "containerReplica",
            [ResourceAttributeNames.RuntimeContainerName] = instance.Name,
            [ResourceAttributeNames.RuntimeReplicaOrdinal] = instance.ReplicaOrdinal.ToString(CultureInfo.InvariantCulture),
            [ResourceAttributeNames.RuntimeReplicaCount] = instance.ReplicaCount.ToString(CultureInfo.InvariantCulture),
            [ResourceAttributeNames.RuntimeRevision] = deployment.RevisionId,
            [ResourceAttributeNames.RuntimeMaterialization] = "orchestratorMaterialized"
        };

        AddIfNotEmpty(attributes, ResourceAttributeNames.ContainerImage, service.Workload.Image);
        AddIfNotEmpty(attributes, ResourceAttributeNames.ContainerHostId, service.Workload.ContainerHostId);

        return new Resource(
            CreateRuntimeContainerResourceId(application.Id, instance.ReplicaOrdinal),
            instance.Name,
            "Container replica",
            "Applications",
            "local",
            state,
            CreateRuntimeContainerEndpoints(application, service),
            deployment.RevisionId,
            DateTimeOffset.UtcNow,
            [],
            ParentResourceId: application.Id,
            TypeId: "runtime.container",
            HealthChecks: CreateRuntimeContainerHealthChecks(application, instance),
            ResourceClass: ResourceClass.Container,
            Attributes: attributes,
            Capabilities:
            [
                new(ResourceCapabilityIds.Monitoring),
                new(ResourceCapabilityIds.LogSources)
            ],
            LogSources: ApplicationLogSources.CreateRuntimeContainerLogSources(
                application.Id,
                instance,
                ApplicationLogSources.GetPrimaryApplicationLogSource(application)),
            EndpointNetworkMappings: CreateRuntimeContainerEndpointNetworkMappings(application, service, instance, state),
            Source: ResourceSource.Orchestrator,
            ManagementMode: ResourceManagementMode.RuntimeManaged,
            Visibility: ResourceVisibility.Hidden,
            OwnerResourceId: application.Id,
            CleanupBehavior: ResourceCleanupBehavior.DeleteWithOwner);
    }

    private static IReadOnlyList<ResourceHealthCheck> CreateRuntimeContainerHealthChecks(
        ApplicationResourceDefinition application,
        ResourceOrchestratorServiceInstance instance)
    {
        if (!ApplicationResourceTypes.IsContainerApp(application.ResourceType) ||
            !IsReplicaModeEnabled(application) ||
            application.HealthChecks.Count == 0)
        {
            return [];
        }

        return application.HealthChecks;
    }

    private static IReadOnlyList<ResourceEndpoint> CreateRuntimeContainerEndpoints(
        ApplicationResourceDefinition application,
        ResourceOrchestratorService service)
    {
        if (!ShouldProjectRuntimeContainerProbeTargets(application))
        {
            return [];
        }

        return GetRuntimeContainerProbePorts(application, service)
            .Select(port => ResourceEndpoint.Contract(
                port.Name,
                NormalizeProtocol(port.Protocol),
                ResourceExposureScope.Local,
                Math.Max(1, port.TargetPort)))
            .ToArray();
    }

    private IReadOnlyList<ResourceEndpointNetworkMapping> CreateRuntimeContainerEndpointNetworkMappings(
        ApplicationResourceDefinition application,
        ResourceOrchestratorService service,
        ResourceOrchestratorServiceInstance instance,
        ResourceState state)
    {
        if (!ShouldProjectActiveRuntimeContainerProbeTargets(application, state))
        {
            return [];
        }

        var resourceId = CreateRuntimeContainerResourceId(application.Id, instance.ReplicaOrdinal);
        return GetRuntimeContainerProbePorts(application, service)
            .Select(port => ResourceEndpointNetworkMapping.ForEndpoint(
                resourceId,
                port.Name,
                CreateRuntimeContainerProbeEndpointAddress(application.Id, port, instance),
                ResourceExposureScope.Local,
                networkResourceId: NormalizeNullable(port.NetworkResourceId),
                sourceEndpointName: port.Name))
            .ToArray();
    }
}
