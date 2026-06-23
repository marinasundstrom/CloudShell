using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Abstractions.Tests;

public sealed class ResourceOrchestratorReplicaGroupsTests
{
    [Fact]
    public void CreateDefaultReplicaGroup_UsesStableServiceReplicaNames()
    {
        var service = CreateService(replicas: 3);

        var replicaGroup = ResourceOrchestratorReplicaGroups.CreateDefaultReplicaGroup(service);
        var instances = replicaGroup.Instances;

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
    public void CreateDefaultReplicaGroup_UsesRevisionScopedReplicaNamesWhenServiceCarriesRuntimeRevision()
    {
        var service = CreateService(replicas: 3) with
        {
            RuntimeRevisionId = "Rev_20260623.1"
        };

        var replicaGroup = ResourceOrchestratorReplicaGroups.CreateDefaultReplicaGroup(service);
        var instances = replicaGroup.Instances;

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
    public void CreateRevisionReplicaGroup_UsesRevisionScopedSingleReplicaName()
    {
        var service = CreateService(replicas: 1);

        var replicaGroup = ResourceOrchestratorReplicaGroups.CreateRevisionReplicaGroup(service, "rev-2");
        var instance = Assert.Single(replicaGroup.Instances);

        Assert.Equal("cloudshell-application-api-rev-2-replicas", replicaGroup.Id);
        Assert.Equal(service.Name, replicaGroup.ServiceId);
        Assert.Equal("rev-2", replicaGroup.RuntimeRevisionId);
        Assert.Equal(1, replicaGroup.RequestedReplicas);
        Assert.Equal(1, replicaGroup.MaterializedReplicas);
        Assert.Equal("cloudshell-application-api-rev-2", instance.Name);
        Assert.Equal("rev-2", instance.RuntimeRevisionId);
    }

    [Fact]
    public void CreateDefaultInstances_ReturnsInstancesFromDefaultReplicaGroup()
    {
        var service = CreateService(replicas: 2);

        var replicaGroup = ResourceOrchestratorReplicaGroups.CreateDefaultReplicaGroup(service);
        var instances = ResourceOrchestratorServiceInstances.CreateDefaultInstances(service);

        Assert.Equal(replicaGroup.Instances, instances);
    }

    [Fact]
    public void CreateChange_ReturnsAddedInstancesForScaleUp()
    {
        var previous = ResourceOrchestratorReplicaGroups.CreateDefaultReplicaGroup(CreateService(replicas: 2));
        var target = ResourceOrchestratorReplicaGroups.CreateDefaultReplicaGroup(CreateService(replicas: 4));

        var change = ResourceOrchestratorReplicaGroups.CreateChange(previous, target);

        Assert.True(change.HasChanges);
        Assert.Equal([3, 4], change.AddedInstances.Select(instance => instance.ReplicaOrdinal).ToArray());
        Assert.Equal([1, 2], change.RetainedInstances.Select(instance => instance.ReplicaOrdinal).ToArray());
        Assert.Empty(change.RemovedInstances);
    }

    [Fact]
    public void CreateChange_ReturnsRemovedInstancesForScaleDown()
    {
        var previous = ResourceOrchestratorReplicaGroups.CreateDefaultReplicaGroup(CreateService(replicas: 4));
        var target = ResourceOrchestratorReplicaGroups.CreateDefaultReplicaGroup(CreateService(replicas: 2));

        var change = ResourceOrchestratorReplicaGroups.CreateChange(previous, target);

        Assert.True(change.HasChanges);
        Assert.Empty(change.AddedInstances);
        Assert.Equal([1, 2], change.RetainedInstances.Select(instance => instance.ReplicaOrdinal).ToArray());
        Assert.Equal([3, 4], change.RemovedInstances.Select(instance => instance.ReplicaOrdinal).ToArray());
    }

    [Fact]
    public void CreateChange_ReturnsNoChangesForMatchingGroups()
    {
        var previous = ResourceOrchestratorReplicaGroups.CreateDefaultReplicaGroup(CreateService(replicas: 2));
        var target = ResourceOrchestratorReplicaGroups.CreateDefaultReplicaGroup(CreateService(replicas: 2));

        var change = ResourceOrchestratorReplicaGroups.CreateChange(previous, target);

        Assert.False(change.HasChanges);
        Assert.Empty(change.AddedInstances);
        Assert.Equal([1, 2], change.RetainedInstances.Select(instance => instance.ReplicaOrdinal).ToArray());
        Assert.Empty(change.RemovedInstances);
    }

    [Fact]
    public void CreateChange_ReplacesAllInstancesWhenGroupIdChanges()
    {
        var previous = ResourceOrchestratorReplicaGroups.CreateRevisionReplicaGroup(CreateService(replicas: 2), "rev-a");
        var target = ResourceOrchestratorReplicaGroups.CreateRevisionReplicaGroup(CreateService(replicas: 2), "rev-b");

        var change = ResourceOrchestratorReplicaGroups.CreateChange(previous, target);

        Assert.True(change.HasChanges);
        Assert.Equal([1, 2], change.AddedInstances.Select(instance => instance.ReplicaOrdinal).ToArray());
        Assert.Empty(change.RetainedInstances);
        Assert.Equal([1, 2], change.RemovedInstances.Select(instance => instance.ReplicaOrdinal).ToArray());
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
