using System.Globalization;
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
