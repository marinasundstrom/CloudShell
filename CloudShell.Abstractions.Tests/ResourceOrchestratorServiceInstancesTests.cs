using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Abstractions.Tests;

public sealed class ResourceOrchestratorServiceInstancesTests
{
    [Fact]
    public void CreateDefaultInstances_UsesStableServiceReplicaNames()
    {
        var service = CreateService(replicas: 3);

        var replicaGroup = ResourceOrchestratorServiceInstances.CreateDefaultReplicaGroup(service);
        var instances = ResourceOrchestratorServiceInstances.CreateDefaultInstances(service);

        Assert.Equal("cloudshell-application-api-replicas", replicaGroup.Id);
        Assert.Equal(service.Name, replicaGroup.ServiceId);
        Assert.Null(replicaGroup.RuntimeRevisionId);
        Assert.Equal(3, replicaGroup.RequestedReplicas);
        Assert.Equal(3, replicaGroup.MaterializedReplicas);
        Assert.Equal(replicaGroup.Instances, instances);
        Assert.Equal(
            [
                "cloudshell-application-api-replica-1",
                "cloudshell-application-api-replica-2",
                "cloudshell-application-api-replica-3"
            ],
            instances.Select(instance => instance.Name).ToArray());
        Assert.All(instances, instance => Assert.Null(instance.RuntimeRevisionId));
    }

    [Fact]
    public void CreateDefaultInstances_UsesRevisionScopedReplicaNamesWhenServiceCarriesRuntimeRevision()
    {
        var service = CreateService(replicas: 3) with
        {
            RuntimeRevisionId = "Rev_20260623.1"
        };

        var replicaGroup = ResourceOrchestratorServiceInstances.CreateDefaultReplicaGroup(service);
        var instances = ResourceOrchestratorServiceInstances.CreateDefaultInstances(service);

        Assert.Equal("cloudshell-application-api-rev-20260623-1-replicas", replicaGroup.Id);
        Assert.Equal(service.Name, replicaGroup.ServiceId);
        Assert.Equal("rev-20260623-1", replicaGroup.RuntimeRevisionId);
        Assert.Equal(3, replicaGroup.RequestedReplicas);
        Assert.Equal(3, replicaGroup.MaterializedReplicas);
        Assert.Equal(replicaGroup.Instances, instances);
        Assert.Equal(
            [
                "cloudshell-application-api-rev-20260623-1-replica-1",
                "cloudshell-application-api-rev-20260623-1-replica-2",
                "cloudshell-application-api-rev-20260623-1-replica-3"
            ],
            instances.Select(instance => instance.Name).ToArray());
        Assert.All(instances, instance => Assert.Equal("rev-20260623-1", instance.RuntimeRevisionId));
    }

    [Fact]
    public void CreateRevisionInstances_UsesRevisionScopedSingleReplicaName()
    {
        var service = CreateService(replicas: 1);

        var replicaGroup = ResourceOrchestratorServiceInstances.CreateRevisionReplicaGroup(service, "rev-2");
        var instance = Assert.Single(ResourceOrchestratorServiceInstances.CreateRevisionInstances(service, "rev-2"));

        Assert.Equal("cloudshell-application-api-rev-2-replicas", replicaGroup.Id);
        Assert.Equal(service.Name, replicaGroup.ServiceId);
        Assert.Equal("rev-2", replicaGroup.RuntimeRevisionId);
        Assert.Equal(1, replicaGroup.RequestedReplicas);
        Assert.Equal(1, replicaGroup.MaterializedReplicas);
        Assert.Equal("cloudshell-application-api-rev-2", instance.Name);
        Assert.Equal("rev-2", instance.RuntimeRevisionId);
    }

    private static ResourceOrchestratorService CreateService(int replicas) =>
        new(
            "application:api",
            "cloudshell-application-api",
            new ResourceWorkloadConfiguration(
                ResourceWorkloadKind.ContainerImage,
                "api",
                Image: "example/api:latest",
                Replicas: replicas,
                ReplicasEnabled: replicas > 1));
}
