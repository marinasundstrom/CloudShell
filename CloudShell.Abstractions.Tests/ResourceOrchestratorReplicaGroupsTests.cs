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
        Assert.Equal(3, replicaGroup.RequestedReplicaSlots);
        Assert.Equal(3, replicaGroup.MaterializedReplicas);
        Assert.Equal(3, replicaGroup.OccupiedReplicaSlots);
        Assert.True(replicaGroup.HasRequestedSlotsMaterialized);
        Assert.Equal(
            ResourceOrchestratorReplicaRestartMode.ReplaceOccupant,
            replicaGroup.EffectiveManagementPolicy.RestartMode);
        Assert.Equal(replicaGroup.Instances, instances);
        Assert.Equal([1, 2, 3], replicaGroup.Slots.Select(slot => slot.Ordinal).ToArray());
        Assert.All(replicaGroup.Slots, slot => Assert.True(slot.IsOccupied));
        Assert.All(replicaGroup.Slots, slot => Assert.Equal(3, slot.SlotCount));
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
    public void CreateDefaultReplicaGroup_UsesServiceReplicaManagementPolicy()
    {
        var policy = new ResourceOrchestratorReplicaManagementPolicy(
            ResourceOrchestratorReplicaRestartMode.LeaveVacant,
            FailureThreshold: 3,
            MaxAttempts: 0);
        var service = CreateService(replicas: 2) with
        {
            ReplicaManagementPolicy = policy
        };

        var replicaGroup = ResourceOrchestratorReplicaGroups.CreateDefaultReplicaGroup(service);

        Assert.Equal(policy, replicaGroup.ManagementPolicy);
        Assert.Equal(policy, replicaGroup.EffectiveManagementPolicy);
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
        Assert.Equal(3, replicaGroup.RequestedReplicaSlots);
        Assert.Equal(3, replicaGroup.MaterializedReplicas);
        Assert.Equal(3, replicaGroup.OccupiedReplicaSlots);
        Assert.Equal(replicaGroup.Instances, instances);
        Assert.Equal([1, 2, 3], replicaGroup.Slots.Select(slot => slot.Occupant?.ReplicaOrdinal).ToArray());
        Assert.All(replicaGroup.Slots, slot => Assert.Equal("rev-20260623-1", slot.RuntimeRevisionId));
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
        Assert.Equal(1, replicaGroup.RequestedReplicaSlots);
        Assert.Equal(1, replicaGroup.MaterializedReplicas);
        var slot = Assert.Single(replicaGroup.Slots);
        Assert.Equal(1, slot.Ordinal);
        Assert.Equal(instance, slot.Occupant);
        Assert.Equal("cloudshell-application-api-rev-2", instance.Name);
        Assert.Equal("rev-2", instance.RuntimeRevisionId);
    }

    [Fact]
    public void ReplicaGroup_CanRepresentVacantRequestedSlot()
    {
        var occupant = new ResourceOrchestratorServiceInstance(
            "cloudshell-application-api-replica-1",
            1,
            3);
        var replicaGroup = new ResourceOrchestratorReplicaGroup(
            "cloudshell-application-api-replicas",
            "cloudshell-application-api",
            RuntimeRevisionId: null,
            RequestedReplicaSlots: 3,
            Instances: [occupant]);

        Assert.Equal(3, replicaGroup.RequestedReplicaSlots);
        Assert.Equal(1, replicaGroup.MaterializedReplicas);
        Assert.Equal(1, replicaGroup.OccupiedReplicaSlots);
        Assert.False(replicaGroup.HasRequestedSlotsMaterialized);
        Assert.Equal([true, false, false], replicaGroup.Slots.Select(slot => slot.IsOccupied).ToArray());
        Assert.Equal([1, 2, 3], replicaGroup.Slots.Select(slot => slot.Ordinal).ToArray());
        Assert.Equal(occupant, replicaGroup.Slots[0].Occupant);
        Assert.Null(replicaGroup.Slots[1].Occupant);
        Assert.Null(replicaGroup.Slots[2].Occupant);
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
