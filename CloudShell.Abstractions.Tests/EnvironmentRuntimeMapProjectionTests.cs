using System.Globalization;
using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Hosting.ResourceManager;

namespace CloudShell.Abstractions.Tests;

public sealed class EnvironmentRuntimeMapProjectionTests
{
    [Fact]
    public void Create_GroupsContainerAppAsManagedOrchestrationService()
    {
        var app = CreateContainerApp();
        var replicas = Enumerable.Range(1, 3)
            .Select(replica => CreateReplica(app, replica, 3))
            .ToArray();
        var serviceId = ResourceOrchestratorReplicaGroups.CreateDefaultServiceName(app.Id);
        var replicaGroupId = ResourceOrchestratorReplicaGroups.CreateReplicaGroupId(serviceId, null);

        var map = EnvironmentRuntimeMapProjection.Create(
            [app, .. replicas],
            [],
            [],
            new EnvironmentRuntimeMapProjectionOptions
            {
                CreateResourceDetailUrl = resource => $"/resources/{resource.Id}",
                GetStateClass = state => state == ResourceState.Running ? "state-running" : "state-unknown"
            });

        Assert.DoesNotContain(map.Nodes, node =>
            node.ArtifactKind == EnvironmentRuntimeArtifactKinds.Resource &&
            node.ResourceId == app.Id);

        var serviceNode = Assert.Single(map.Nodes, node =>
            node.ArtifactKind == EnvironmentRuntimeArtifactKinds.OrchestrationService);
        Assert.Equal(app.Id, serviceNode.ResourceId);
        Assert.Equal(serviceId, serviceNode.ServiceId);
        Assert.Equal("/resources/application.container-app:api", serviceNode.DetailUrl);

        var serviceGroup = Assert.Single(map.Groups, group =>
            group.ArtifactKind == EnvironmentRuntimeArtifactKinds.OrchestrationService);
        Assert.Equal(app.Id, serviceGroup.ResourceId);
        Assert.Equal(serviceId, serviceGroup.ServiceId);
        Assert.Equal("Container app", serviceGroup.BadgeLabel);
        Assert.Contains(serviceNode.Id, serviceGroup.NodeIds);

        var replicaGroup = Assert.Single(map.Groups, group =>
            group.ArtifactKind == EnvironmentRuntimeArtifactKinds.ReplicaGroup);
        Assert.Equal(replicaGroupId, replicaGroup.ReplicaGroupId);
        Assert.Equal(serviceGroup.Id, replicaGroup.ParentGroupId);

        var replicaNodes = map.Nodes
            .Where(node => node.ArtifactKind == EnvironmentRuntimeArtifactKinds.Replica)
            .OrderBy(node => node.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.Equal(3, replicaNodes.Length);
        Assert.All(replicaNodes, node =>
        {
            Assert.Equal(serviceId, node.ServiceId);
            Assert.Equal(replicaGroupId, node.ReplicaGroupId);
            Assert.Contains(node.Id, replicaGroup.NodeIds);
        });

        Assert.Contains(map.Links, link =>
            link.Source == serviceNode.Id &&
            link.Target == $"replica-group:{replicaGroupId}" &&
            link.ArtifactKind == EnvironmentRuntimeArtifactKinds.OrchestrationService &&
            link.Scope == EnvironmentRuntimeMapLinkScopes.Internal);
    }

    [Fact]
    public void Create_NestsRoutingBindingsInsideOwningServiceBoundary()
    {
        var app = CreateContainerApp();
        var loadBalancer = CreateLoadBalancer();
        var serviceId = ResourceOrchestratorReplicaGroups.CreateDefaultServiceName(app.Id);
        var replicaGroupId = ResourceOrchestratorReplicaGroups.CreateReplicaGroupId(serviceId, "rev-2");
        var deployment = CreateDeploymentWithRoutingBinding(
            app.Id,
            serviceId,
            replicaGroupId,
            loadBalancer.Id);
        var replicaGroup = new EnvironmentRuntimeMapReplicaGroup(
            app.Id,
            serviceId,
            replicaGroupId,
            "rev-2",
            RequestedSlots: 2,
            OccupiedSlots: 2,
            MaterializedReplicas: 2,
            RepairingCount: 0,
            RepairFailedCount: 0);

        var map = EnvironmentRuntimeMapProjection.Create(
            [app, loadBalancer],
            [deployment],
            [replicaGroup],
            new EnvironmentRuntimeMapProjectionOptions
            {
                CreateResourceDetailUrl = resource => $"/resources/{resource.Id}",
                GetStateClass = state => state == ResourceState.Running ? "state-running" : "state-unknown"
            });

        var routingNode = Assert.Single(map.Nodes, node =>
            node.ArtifactKind == EnvironmentRuntimeArtifactKinds.RoutingBinding);
        Assert.Equal(serviceId, routingNode.ServiceId);
        Assert.Equal(replicaGroupId, routingNode.ReplicaGroupId);
        Assert.Equal("rev-2", routingNode.RuntimeRevisionId);

        var serviceGroup = Assert.Single(map.Groups, group =>
            group.ArtifactKind == EnvironmentRuntimeArtifactKinds.OrchestrationService);
        Assert.Contains(routingNode.Id, serviceGroup.NodeIds);

        var serviceNode = Assert.Single(map.Nodes, node =>
            node.ArtifactKind == EnvironmentRuntimeArtifactKinds.OrchestrationService);
        Assert.Contains(map.Links, link =>
            link.Source == serviceNode.Id &&
            link.Target == routingNode.Id &&
            link.Label == "routes" &&
            link.Scope == EnvironmentRuntimeMapLinkScopes.Internal);
        Assert.Contains(map.Links, link =>
            link.Source == routingNode.Id &&
            link.Target == $"replica-group:{replicaGroupId}" &&
            link.Label == "targets" &&
            link.ArtifactKind == EnvironmentRuntimeArtifactKinds.RoutingBinding);
        Assert.Contains(map.Links, link =>
            link.Source == $"resource:{loadBalancer.Id}" &&
            link.Target == routingNode.Id &&
            link.Label == "materializes" &&
            link.ArtifactKind == EnvironmentRuntimeArtifactKinds.RoutingBinding);
    }

    private static Resource CreateContainerApp() =>
        new(
            "application.container-app:api",
            "api",
            "application.container-app",
            "test",
            "local",
            ResourceState.Running,
            [],
            "1",
            DateTimeOffset.UtcNow,
            [],
            TypeId: "application.container-app",
            ResourceClass: ResourceClass.Service,
            Attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ResourceAttributeNames.ContainerImage] = "api:latest",
                [ResourceAttributeNames.ContainerReplicas] = "3"
            },
            DisplayName: "Replicated API");

    private static Resource CreateLoadBalancer() =>
        new(
            "cloudshell.loadBalancer:public",
            "public",
            "cloudshell.loadBalancer",
            "test",
            "local",
            ResourceState.Running,
            [],
            "1",
            DateTimeOffset.UtcNow,
            [],
            TypeId: "cloudshell.loadBalancer",
            ResourceClass: ResourceClass.Network,
            DisplayName: "Public load balancer");

    private static ResourceDeploymentRecord CreateDeploymentWithRoutingBinding(
        string resourceId,
        string serviceId,
        string replicaGroupId,
        string? loadBalancerResourceId = null)
    {
        var startedAt = new DateTimeOffset(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);
        var routingBinding = new ResourceOrchestratorServiceRoutingBindingDefinition(
            "api-http-routing",
            ResourceOrchestratorDeploymentDefinition.CurrentDefinitionVersion,
            serviceId,
            replicaGroupId,
            ResourceEndpointReference.ForEndpoint(resourceId, "http"),
            LoadBalancerResourceId: loadBalancerResourceId);
        return new ResourceDeploymentRecord(
            "api-deployment",
            "default",
            resourceId,
            serviceId,
            "rev-2",
            ResourceOrchestratorDeploymentStatus.Active,
            startedAt,
            CompletedAt: startedAt.AddSeconds(5),
            ReplicaGroup: new ResourceOrchestratorReplicaGroup(
                replicaGroupId,
                serviceId,
                "rev-2",
                2,
                [
                    new ResourceOrchestratorServiceInstance("api-rev-2-replica-1", 1, 2, "rev-2"),
                    new ResourceOrchestratorServiceInstance("api-rev-2-replica-2", 2, 2, "rev-2")
                ]),
            Definition: new ResourceOrchestratorDeploymentDefinition(
                ResourceOrchestratorDeploymentDefinition.CurrentDefinitionVersion,
                Services:
                [
                    new ResourceOrchestratorServiceDefinition(
                        serviceId,
                        ResourceOrchestratorDeploymentDefinitionTypes.Service,
                        ResourceOrchestratorDeploymentDefinition.CurrentDefinitionVersion,
                        Resources:
                        [
                            routingBinding.ToResourceDefinition()
                        ])
                ]));
    }

    private static Resource CreateReplica(Resource owner, int replica, int replicas)
    {
        var replicaName = $"api replica {replica.ToString(CultureInfo.InvariantCulture)}";
        return new Resource(
            $"runtime.container:api-{replica.ToString(CultureInfo.InvariantCulture)}",
            replicaName,
            "runtime.container",
            "test",
            "local",
            ResourceState.Running,
            [],
            "1",
            DateTimeOffset.UtcNow,
            [],
            ParentResourceId: owner.Id,
            TypeId: "runtime.container",
            ResourceClass: ResourceClass.Container,
            Attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ResourceAttributeNames.RuntimeKind] = "containerReplica",
                [ResourceAttributeNames.RuntimeContainerName] = replicaName,
                [ResourceAttributeNames.RuntimeReplicaOrdinal] = replica.ToString(CultureInfo.InvariantCulture),
                [ResourceAttributeNames.RuntimeReplicaCount] = replicas.ToString(CultureInfo.InvariantCulture)
            },
            ManagementMode: ResourceManagementMode.RuntimeManaged,
            Visibility: ResourceVisibility.Hidden,
            OwnerResourceId: owner.Id,
            DisplayName: replicaName);
    }
}
