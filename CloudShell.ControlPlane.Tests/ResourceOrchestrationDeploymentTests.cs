using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Logs;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.ControlPlane.ResourceManager.Deployment;
using CloudShell.ControlPlane.ResourceManager.Orchestration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace CloudShell.ControlPlane.Tests;

public sealed class ResourceOrchestrationDeploymentTests
{
    [Fact]
    public async Task ApplyDeploymentAsync_AppliesDeploymentServiceSpec()
    {
        var resource = CreateResource();
        var provider = new RecordingServiceProcedureProvider(resource);
        var resourceEvents = new InMemoryResourceEventStore();
        var deploymentStore = new InMemoryResourceOrchestratorDeploymentStore();
        var deployments = CreateDeployments(resource, provider, resourceEvents, deploymentStore);
        var deployment = CreateDeployment(resource.Id, "default", replicas: 3);

        var result = await deployments.ApplyDeploymentAsync(
            resource,
            deployment,
            triggeredBy: "tests",
            cause: "Container app deployment.");

        Assert.Equal(ResourceOrchestratorDeploymentStatus.Active, result.Deployment.Status);
        Assert.NotNull(result.Deployment.Spec.Definition);
        Assert.NotEqual(deployment.RevisionId, result.Revision.Id.ToString());
        Assert.StartsWith("env-", result.Revision.Id.ToString(), StringComparison.Ordinal);
        Assert.Equal(deployment.Id, result.Revision.DeploymentId);
        Assert.Equal(deployment.SourceResourceId, result.Revision.SourceResourceId);
        Assert.Equal(deployment.ServiceId, result.Revision.ServiceId);
        Assert.Null(result.Deployment.BasedOnRevisionId);
        Assert.Null(result.Revision.BasedOnRevisionId);
        Assert.Equal("tests", result.Revision.ProvisionedBy);
        Assert.Equal(1, result.Revision.RevisionNumber);
        Assert.Equal(ResourceOrchestratorRevisionStatus.Active, result.Revision.Status);
        Assert.Same(result.Deployment.Spec.Definition, result.Revision.Definition);
        Assert.NotNull(result.Revision.ReplicaGroup);
        Assert.Equal("cloudshell-application-api-rev-2-replicas", result.Revision.ReplicaGroup.Id);
        Assert.Equal(deployment.ServiceId, result.Revision.ReplicaGroup.ServiceId);
        Assert.Equal(deployment.RevisionId, result.Revision.ReplicaGroup.RuntimeRevisionId);
        Assert.Equal(3, result.Revision.ReplicaGroup.RequestedReplicas);
        Assert.Equal(3, result.Revision.ReplicaGroup.MaterializedReplicas);
        Assert.NotNull(result.Revision.Definition);
        var revisionServiceDefinition = Assert.Single(result.Revision.Definition.DeploymentServices);
        Assert.Equal(deployment.Spec.Service.Name, revisionServiceDefinition.Name);
        var revisionReplicaGroupDefinition = Assert.Single(revisionServiceDefinition.ServiceResources);
        Assert.Equal(result.Revision.ReplicaGroup.Id, revisionReplicaGroupDefinition.Name);
        Assert.Equal(
            ResourceOrchestratorDeploymentDefinitionTypes.ReplicaGroup,
            revisionReplicaGroupDefinition.Type);
        var preparedService = Assert.Single(provider.PreparedServices);
        Assert.Equal(deployment.Spec.Service.Name, preparedService.Name);
        Assert.Equal(deployment.RevisionId, preparedService.RuntimeRevisionId);
        var preparedContext = Assert.Single(provider.PreparedContexts);
        Assert.NotNull(preparedContext.ReplicaGroup);
        Assert.Equal("cloudshell-application-api-rev-2-replicas", preparedContext.ReplicaGroup.Id);
        Assert.Equal(deployment.ServiceId, preparedContext.ReplicaGroup.ServiceId);
        Assert.Equal(deployment.RevisionId, preparedContext.ReplicaGroup.RuntimeRevisionId);
        Assert.Equal(3, preparedContext.ReplicaGroup.RequestedReplicas);
        Assert.Equal(3, preparedContext.ReplicaGroup.MaterializedReplicas);
        Assert.Equal(
            [1, 2, 3],
            provider.ExecutedInstances
                .Select(instance => instance.Instance.ReplicaOrdinal)
                .Order()
                .ToArray());
        Assert.Equal(
            [
                "cloudshell-application-api-rev-2-replica-1",
                "cloudshell-application-api-rev-2-replica-2",
                "cloudshell-application-api-rev-2-replica-3"
            ],
            provider.ExecutedInstances
                .Select(instance => instance.Instance.Name)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray());
        Assert.All(
            provider.ExecutedInstances,
            instance => Assert.Equal(deployment.RevisionId, instance.Instance.RuntimeRevisionId));
        Assert.All(
            provider.ExecutedInstances,
            instance => Assert.Equal(preparedContext.ReplicaGroup, instance.ReplicaGroup));
        Assert.All(
            provider.ExecutedInstances,
            instance => Assert.Equal(deployment.Spec.Service.Name, instance.Service.Name));
        var events = resourceEvents
            .GetEvents(new ResourceEventQuery(ResourceId: resource.Id))
            .Reverse()
            .ToArray();
        Assert.Equal(
            [
                ResourceEventTypes.Events.Deployment.Applying,
                ResourceEventTypes.Events.Deployment.ServiceReconciling,
                ResourceEventTypes.Events.Deployment.ServiceReconciled,
                ResourceEventTypes.Events.Deployment.ReplicaMaterializing,
                ResourceEventTypes.Events.Deployment.ReplicaMaterialized,
                ResourceEventTypes.Events.Deployment.ReplicaMaterializing,
                ResourceEventTypes.Events.Deployment.ReplicaMaterialized,
                ResourceEventTypes.Events.Deployment.ReplicaMaterializing,
                ResourceEventTypes.Events.Deployment.ReplicaMaterialized,
                ResourceEventTypes.Events.Deployment.RoutingUpdating,
                ResourceEventTypes.Events.Deployment.RoutingUpdated,
                ResourceEventTypes.Events.Deployment.Applied
            ],
            events.Select(resourceEvent => resourceEvent.EventType).ToArray());
        Assert.All(
            events,
            resourceEvent => Assert.Equal("tests", resourceEvent.TriggeredBy));
        Assert.All(
            events.Where(resourceEvent =>
                resourceEvent.EventType != ResourceEventTypes.Events.Deployment.Applied),
            resourceEvent => Assert.Contains(
                "Cause: Container app deployment.",
                resourceEvent.Message,
                StringComparison.Ordinal));
        Assert.Contains(
            events,
            resourceEvent =>
                resourceEvent.EventType == ResourceEventTypes.Events.Deployment.ReplicaMaterializing &&
                resourceEvent.Message.Contains("replica 2/3", StringComparison.Ordinal));
        var deploymentRecord = Assert.Single(deploymentStore.List(new ResourceOrchestratorDeploymentQuery(
            SourceResourceId: resource.Id,
            DeploymentId: deployment.Id)));
        Assert.Equal(ResourceOrchestratorDeploymentStatus.Active, deploymentRecord.Status);
        Assert.Null(deploymentRecord.Deployment.BasedOnRevisionId);
        Assert.Equal(result.Revision, deploymentRecord.Revision);
        Assert.Equal("tests", deploymentRecord.Revision?.ProvisionedBy);
        Assert.Equal(result.Revision.ReplicaGroup, deploymentRecord.ReplicaGroup);
        Assert.Equal(result.Revision.Definition, deploymentRecord.Revision?.Definition);
        Assert.Equal("tests", deploymentRecord.TriggeredBy);
        Assert.Equal("Container app deployment.", deploymentRecord.Cause);
        Assert.Equal(result.ProcedureResult.Message, deploymentRecord.Message);
        Assert.NotNull(deploymentRecord.CompletedAt);
    }

    [Fact]
    public async Task ApplyDeploymentAsync_UsesReplicaGroupDefinitionAsDesiredState()
    {
        var resource = CreateResource();
        var provider = new RecordingServiceProcedureProvider(resource);
        var deploymentStore = new InMemoryResourceOrchestratorDeploymentStore();
        var deployments = CreateDeployments(resource, provider, deploymentStore: deploymentStore);
        var baseDeployment = CreateDeployment(resource.Id, "default", replicas: 5);
        var replicaGroup = ResourceOrchestratorReplicaGroupDefinition
            .FromReplicaGroup(
                ResourceOrchestratorReplicaGroups.CreateRevisionReplicaGroup(
                    baseDeployment.Spec.Service with
                    {
                        Workload = baseDeployment.Spec.Service.Workload with
                        {
                            Replicas = 2,
                            ReplicasEnabled = true
                        }
                    },
                    "rev-9"),
                "rev-9")
            with
            {
                Name = "custom-api-rev-9-slots"
            };
        var deployment = baseDeployment with
        {
            RevisionId = "rev-9",
            Spec = baseDeployment.Spec with
            {
                Definition = new ResourceOrchestratorDeploymentDefinition(
                    ResourceOrchestratorDeploymentDefinition.CurrentDefinitionVersion,
                    Services:
                    [
                        new ResourceOrchestratorServiceDefinition(
                            baseDeployment.Spec.Service.Name,
                            ResourceOrchestratorDeploymentDefinitionTypes.Service,
                            ResourceOrchestratorDeploymentDefinition.CurrentDefinitionVersion,
                            Resources: [replicaGroup.ToResourceDefinition()])
                    ])
            }
        };

        var result = await deployments.ApplyDeploymentAsync(resource, deployment);

        Assert.NotNull(result.Revision.ReplicaGroup);
        Assert.Equal("custom-api-rev-9-slots", result.Revision.ReplicaGroup.Id);
        Assert.Equal("rev-9", result.Revision.ReplicaGroup.RuntimeRevisionId);
        Assert.Equal(2, result.Revision.ReplicaGroup.RequestedReplicaSlots);
        Assert.Equal(
            [
                "cloudshell-application-api-rev-9-replica-1",
                "cloudshell-application-api-rev-9-replica-2"
            ],
            provider.ExecutedInstances
                .Select(instance => instance.Instance.Name)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray());
        Assert.Equal(
            ["Route:custom-api-rev-9-slots"],
            provider.Operations
                .Where(operation => operation.StartsWith("Route:", StringComparison.Ordinal))
                .ToArray());
    }

    [Fact]
    public async Task ApplyDeploymentAsync_RematerializesSameReplicaGroupWhenResourceIsStopped()
    {
        var deploymentStore = new InMemoryResourceOrchestratorDeploymentStore();
        var running = CreateResource();
        var initialProvider = new RecordingServiceProcedureProvider(running);
        var initialDeployments = CreateDeployments(running, initialProvider, deploymentStore: deploymentStore);
        var deployment = CreateDeployment(running.Id, "default", replicas: 2);
        await initialDeployments.ApplyDeploymentAsync(running, deployment);

        var stopped = CreateResource(state: ResourceState.Stopped);
        var restartProvider = new RecordingServiceProcedureProvider(stopped);
        var restartDeployments = CreateDeployments(stopped, restartProvider, deploymentStore: deploymentStore);

        var result = await restartDeployments.ApplyDeploymentAsync(stopped, deployment);

        Assert.Equal(ResourceOrchestratorDeploymentStatus.Active, result.Deployment.Status);
        Assert.Equal(
            [
                "cloudshell-application-api-rev-2-replica-1",
                "cloudshell-application-api-rev-2-replica-2"
            ],
            restartProvider.ExecutedInstances
                .Select(instance => instance.Instance.Name)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray());
        Assert.DoesNotContain(
            restartProvider.ExecutedActions,
            action => action.Kind == ResourceActionKind.Stop);
    }

    [Fact]
    public async Task ApplyDeploymentAsync_RejectsProviderIdAsOrchestratorId()
    {
        var resource = CreateResource();
        var provider = new RecordingServiceProcedureProvider(resource);
        var deployments = CreateDeployments(resource, provider);
        var deployment = CreateDeployment(resource.Id, provider.Id, replicas: 1);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            deployments.ApplyDeploymentAsync(resource, deployment));

        Assert.Equal(ControlPlaneErrorCodes.InvalidRequest, exception.Error.Code);
        Assert.Contains(
            $"Orchestrator '{provider.Id}' is not registered for deployment '{deployment.Id}'",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Empty(provider.PreparedServices);
        Assert.Empty(provider.ExecutedInstances);
    }

    [Fact]
    public async Task ApplyDeploymentAsync_UsesDefaultDeploymentServiceForOrchestratorWithoutNativeDeployments()
    {
        var resource = CreateResource();
        var provider = new RecordingServiceProcedureProvider(resource);
        var deploymentStore = new InMemoryResourceOrchestratorDeploymentStore();
        var deployments = CreateDeployments(
            resource,
            provider,
            deploymentStore: deploymentStore,
            orchestrators: [new DefaultResourceOrchestrator(), new PassiveResourceOrchestrator("passthrough")]);
        var deployment = CreateDeployment(resource.Id, "passthrough", replicas: 2);

        var result = await deployments.ApplyDeploymentAsync(resource, deployment);

        Assert.Equal(ResourceOrchestratorDeploymentStatus.Active, result.Deployment.Status);
        Assert.Equal("passthrough", result.Deployment.OrchestratorId);
        Assert.Equal(2, result.Revision.ReplicaGroup?.RequestedReplicas);
        Assert.Equal(
            [
                "cloudshell-application-api-rev-2-replica-1",
                "cloudshell-application-api-rev-2-replica-2"
            ],
            provider.ExecutedInstances
                .Select(instance => instance.Instance.Name)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray());
        var deploymentRecord = Assert.Single(deploymentStore.List(new ResourceOrchestratorDeploymentQuery(
            SourceResourceId: resource.Id,
            DeploymentId: deployment.Id)));
        Assert.Equal("passthrough", deploymentRecord.OrchestratorId);
        Assert.Equal(ResourceOrchestratorDeploymentStatus.Active, deploymentRecord.Status);
    }

    [Fact]
    public async Task ApplyDeploymentAsync_ReconcilesReplicaGroupCapacityForActiveRuntimeRevision()
    {
        var resource = CreateResource();
        var provider = new RecordingServiceProcedureProvider(resource);
        var deploymentStore = new InMemoryResourceOrchestratorDeploymentStore();
        var deployments = CreateDeployments(resource, provider, deploymentStore: deploymentStore);

        await deployments.ApplyDeploymentAsync(
            resource,
            CreateDeployment(resource.Id, "default", replicas: 2),
            triggeredBy: "tests",
            cause: "Initial deployment.");
        await deployments.ApplyDeploymentAsync(
            resource,
            CreateDeployment(resource.Id, "default", replicas: 4),
            triggeredBy: "tests",
            cause: "Scale up.");
        await deployments.ApplyDeploymentAsync(
            resource,
            CreateDeployment(resource.Id, "default", replicas: 2),
            triggeredBy: "tests",
            cause: "Scale down.");

        Assert.Equal(
            [
                "Start:cloudshell-application-api-rev-2-replica-1",
                "Start:cloudshell-application-api-rev-2-replica-2",
                "Start:cloudshell-application-api-rev-2-replica-3",
                "Start:cloudshell-application-api-rev-2-replica-4",
                "Stop:cloudshell-application-api-rev-2-replica-4",
                "Stop:cloudshell-application-api-rev-2-replica-3"
            ],
            provider.ExecutedInstanceActions.ToArray());
        var records = deploymentStore.List(new ResourceOrchestratorDeploymentQuery(
            SourceResourceId: resource.Id,
            DeploymentId: "cloudshell-application-api-deployment",
            MaxRecords: 10));
        Assert.Equal(3, records.Count);
        Assert.Equal([2, 4, 2], records
            .OrderBy(record => record.CompletedAt ?? record.StartedAt)
            .Select(record => record.ReplicaGroup?.RequestedReplicas ?? 0)
            .ToArray());
        Assert.Equal([1, 2, 3], records
            .OrderBy(record => record.CompletedAt ?? record.StartedAt)
            .Select(record => record.Revision?.RevisionNumber ?? 0)
            .ToArray());
    }

    [Fact]
    public async Task DeploymentCoordinator_AppliesThroughRecordedDeploymentPath()
    {
        var resource = CreateResource();
        var provider = new RecordingServiceProcedureProvider(resource);
        var deploymentStore = new InMemoryResourceOrchestratorDeploymentStore();
        IResourceOrchestratorDeploymentCoordinator coordinator =
            CreateDeployments(resource, provider, deploymentStore: deploymentStore);
        var initialDeployment = CreateDeployment(resource.Id, "default", replicas: 2);
        var scaleDeployment = CreateDeployment(resource.Id, "default", replicas: 4);

        Assert.True(coordinator.CanApplyDeployment(resource, initialDeployment));

        var initialResult = await coordinator.ApplyDeploymentAsync(
            resource,
            initialDeployment,
            triggeredBy: "tests",
            cause: "Initial coordinator deployment.");
        var scaleResult = await coordinator.ApplyDeploymentAsync(
            resource,
            scaleDeployment,
            triggeredBy: "tests",
            cause: "Coordinator scale deployment.");

        Assert.Equal(initialResult.Revision.Id, scaleResult.Deployment.BasedOnRevisionId);
        Assert.Equal(initialResult.Revision.Id, scaleResult.Revision.BasedOnRevisionId);
        Assert.NotNull(scaleResult.PreviousReplicaGroup);
        Assert.Equal(2, scaleResult.PreviousReplicaGroup.RequestedReplicaSlots);
        Assert.Equal(4, scaleResult.Revision.ReplicaGroup?.RequestedReplicaSlots);
        var records = deploymentStore.List(new ResourceOrchestratorDeploymentQuery(
            SourceResourceId: resource.Id,
            DeploymentId: initialDeployment.Id,
            MaxRecords: 10));
        Assert.Equal(2, records.Count);
        Assert.All(records, record =>
            Assert.Equal(ResourceOrchestratorDeploymentStatus.Active, record.Status));
        Assert.Equal(
            [2, 4],
            records
                .OrderBy(record => record.CompletedAt ?? record.StartedAt)
                .Select(record => record.ReplicaGroup?.RequestedReplicaSlots ?? 0)
                .ToArray());
    }

    [Fact]
    public async Task ApplyDeploymentAsync_UpdatesRoutingAfterAddingReplicas()
    {
        var resource = CreateResource();
        var deploymentStore = new InMemoryResourceOrchestratorDeploymentStore();
        var initialProvider = new RecordingServiceProcedureProvider(resource);
        var initialDeployments = CreateDeployments(resource, initialProvider, deploymentStore: deploymentStore);
        await initialDeployments.ApplyDeploymentAsync(
            resource,
            CreateDeployment(resource.Id, "default", replicas: 2));

        var scaleProvider = new RecordingServiceProcedureProvider(resource);
        var scaleDeployments = CreateDeployments(resource, scaleProvider, deploymentStore: deploymentStore);

        await scaleDeployments.ApplyDeploymentAsync(
            resource,
            CreateDeployment(resource.Id, "default", replicas: 4));

        Assert.Equal(
            [
                "Prepare:Start:cloudshell-application-api-rev-2-replicas",
                "Start:cloudshell-application-api-rev-2-replica-3",
                "Start:cloudshell-application-api-rev-2-replica-4",
                "Route:cloudshell-application-api-rev-2-replicas"
            ],
            scaleProvider.Operations.ToArray());
        Assert.Equal("cloudshell-application-api-rev-2-replicas", Assert.Single(scaleProvider.RoutedContexts).ReplicaGroup?.Id);
    }

    [Fact]
    public async Task ApplyDeploymentAsync_CanUpdateRoutingBeforeAddingReplicasWhenPolicyRequires()
    {
        var resource = CreateResource();
        var deploymentStore = new InMemoryResourceOrchestratorDeploymentStore();
        var initialProvider = new RecordingServiceProcedureProvider(resource);
        var initialDeployments = CreateDeployments(resource, initialProvider, deploymentStore: deploymentStore);
        await initialDeployments.ApplyDeploymentAsync(
            resource,
            CreateDeployment(resource.Id, "default", replicas: 2));

        var scaleProvider = new RecordingServiceProcedureProvider(resource);
        var scaleDeployments = CreateDeployments(resource, scaleProvider, deploymentStore: deploymentStore);
        var policy = ResourceOrchestratorReplicaGroupReconciliationPolicy.Default with
        {
            ScaleOutRoutingMode = ResourceOrchestratorScaleOutRoutingMode.BeforeAddedReplicas
        };

        await scaleDeployments.ApplyDeploymentAsync(
            resource,
            WithReconciliationPolicy(
                CreateDeployment(resource.Id, "default", replicas: 4),
                policy));

        Assert.Equal(
            [
                "Route:cloudshell-application-api-rev-2-replicas",
                "Prepare:Start:cloudshell-application-api-rev-2-replicas",
                "Start:cloudshell-application-api-rev-2-replica-3",
                "Start:cloudshell-application-api-rev-2-replica-4"
            ],
            scaleProvider.Operations.ToArray());
    }

    [Fact]
    public async Task ApplyDeploymentAsync_UpdatesRoutingBeforeRemovingReplicas()
    {
        var resource = CreateResource();
        var deploymentStore = new InMemoryResourceOrchestratorDeploymentStore();
        var initialProvider = new RecordingServiceProcedureProvider(resource);
        var initialDeployments = CreateDeployments(resource, initialProvider, deploymentStore: deploymentStore);
        await initialDeployments.ApplyDeploymentAsync(
            resource,
            CreateDeployment(resource.Id, "default", replicas: 4));

        var scaleProvider = new RecordingServiceProcedureProvider(resource);
        var scaleDeployments = CreateDeployments(resource, scaleProvider, deploymentStore: deploymentStore);

        await scaleDeployments.ApplyDeploymentAsync(
            resource,
            CreateDeployment(resource.Id, "default", replicas: 2));

        Assert.Equal(
            [
                "Route:cloudshell-application-api-rev-2-replicas",
                "Stop:cloudshell-application-api-rev-2-replica-4",
                "Stop:cloudshell-application-api-rev-2-replica-3"
            ],
            scaleProvider.Operations.ToArray());
        Assert.Empty(scaleProvider.PreparedActions);
        Assert.Equal("cloudshell-application-api-rev-2-replicas", Assert.Single(scaleProvider.RoutedContexts).ReplicaGroup?.Id);
    }

    [Fact]
    public async Task ApplyDeploymentAsync_CanUpdateRoutingAfterRemovingReplicasWhenPolicyRequires()
    {
        var resource = CreateResource();
        var deploymentStore = new InMemoryResourceOrchestratorDeploymentStore();
        var initialProvider = new RecordingServiceProcedureProvider(resource);
        var initialDeployments = CreateDeployments(resource, initialProvider, deploymentStore: deploymentStore);
        await initialDeployments.ApplyDeploymentAsync(
            resource,
            CreateDeployment(resource.Id, "default", replicas: 4));

        var scaleProvider = new RecordingServiceProcedureProvider(resource);
        var scaleDeployments = CreateDeployments(resource, scaleProvider, deploymentStore: deploymentStore);
        var policy = ResourceOrchestratorReplicaGroupReconciliationPolicy.Default with
        {
            ScaleInRoutingMode = ResourceOrchestratorScaleInRoutingMode.AfterRemovedReplicas
        };

        await scaleDeployments.ApplyDeploymentAsync(
            resource,
            WithReconciliationPolicy(
                CreateDeployment(resource.Id, "default", replicas: 2),
                policy));

        Assert.Equal(
            [
                "Stop:cloudshell-application-api-rev-2-replica-4",
                "Stop:cloudshell-application-api-rev-2-replica-3",
                "Route:cloudshell-application-api-rev-2-replicas"
            ],
            scaleProvider.Operations.ToArray());
    }

    [Fact]
    public async Task ApplyDeploymentAsync_ReturnsRetiredReplicaGroupWhenRuntimeRevisionChanges()
    {
        var resource = CreateResource();
        var provider = new RecordingServiceProcedureProvider(resource);
        var deploymentStore = new InMemoryResourceOrchestratorDeploymentStore();
        var deployments = CreateDeployments(resource, provider, deploymentStore: deploymentStore);
        var firstDeployment = CreateDeployment(resource.Id, "default", replicas: 2);
        var secondDeployment = CreateDeployment(resource.Id, "default", replicas: 3) with
        {
            RevisionId = "rev-3"
        };

        var firstResult = await deployments.ApplyDeploymentAsync(resource, firstDeployment);
        var secondResult = await deployments.ApplyDeploymentAsync(resource, secondDeployment);

        Assert.Empty(firstResult.ReplicaGroupsToTearDown);
        var tearDown = Assert.Single(secondResult.ReplicaGroupsToTearDown);
        Assert.NotNull(tearDown.ReplicaGroup);
        Assert.Equal(firstResult.Revision.ReplicaGroup, tearDown.ReplicaGroup);
        Assert.Equal("cloudshell-application-api-rev-2-replicas", tearDown.ReplicaGroup.Id);
        Assert.Equal("rev-2", tearDown.Service.RuntimeRevisionId);
        Assert.Equal(2, tearDown.Service.Replicas);
        Assert.Equal(
            [
                "cloudshell-application-api-rev-2-replica-1",
                "cloudshell-application-api-rev-2-replica-2"
            ],
            tearDown.ReplicaGroup.Instances.Select(instance => instance.Name).ToArray());
        Assert.Contains(secondDeployment.Id, tearDown.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "cloudshell-application-api-rev-3-replicas",
            tearDown.Reason,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal("cloudshell-application-api-rev-3-replicas", secondResult.Revision.ReplicaGroup?.Id);
        Assert.DoesNotContain(
            provider.ExecutedInstanceActions,
            action => action.StartsWith("Stop:", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            provider.Operations,
            operation => string.Equals(operation, "Route:cloudshell-application-api-rev-3-replicas", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApplyDeploymentAsync_RetainsConfiguredPreviousReplicaSlotsWhenRuntimeRevisionChanges()
    {
        var resource = CreateResource();
        var provider = new RecordingServiceProcedureProvider(resource);
        var deploymentStore = new InMemoryResourceOrchestratorDeploymentStore();
        var deployments = CreateDeployments(resource, provider, deploymentStore: deploymentStore);
        var firstDeployment = CreateDeployment(resource.Id, "default", replicas: 3);
        var replacementPolicy = ResourceOrchestratorReplicaGroupReconciliationPolicy.Default with
        {
            RetainPreviousReplicaSlots = 1
        };
        var secondDeployment = WithReconciliationPolicy(
            CreateDeployment(resource.Id, "default", replicas: 3) with
            {
                RevisionId = "rev-3"
            },
            replacementPolicy);

        await deployments.ApplyDeploymentAsync(resource, firstDeployment);
        var secondResult = await deployments.ApplyDeploymentAsync(resource, secondDeployment);

        var tearDown = Assert.Single(secondResult.ReplicaGroupsToTearDown);
        Assert.NotNull(tearDown.ReplicaGroup);
        Assert.Equal(2, tearDown.ReplicaGroup.RequestedReplicaSlots);
        Assert.Equal(2, tearDown.Service.Replicas);
        Assert.Equal(
            [
                "cloudshell-application-api-rev-2-replica-2",
                "cloudshell-application-api-rev-2-replica-3"
            ],
            tearDown.ReplicaGroup.Instances.Select(instance => instance.Name).ToArray());
        Assert.Contains("retained 1 previous replica slot", tearDown.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApplyDeploymentAsync_RecordsMaterializedReplicaSlotStates()
    {
        var resource = CreateResource();
        var provider = new RecordingServiceProcedureProvider(resource);
        var deploymentStore = new InMemoryResourceOrchestratorDeploymentStore();
        var reconciliationStore = new InMemoryResourceReplicaGroupReconciliationStore();
        var deployments = CreateDeployments(
            resource,
            provider,
            deploymentStore: deploymentStore,
            reconciliationStore: reconciliationStore);

        var result = await deployments.ApplyDeploymentAsync(
            resource,
            CreateDeployment(resource.Id, "default", replicas: 3),
            triggeredBy: "tests");

        var slotStates = reconciliationStore.ListRuntimeStates(resource.Id);
        Assert.Equal([1, 2, 3], slotStates.Select(state => state.SlotOrdinal).ToArray());
        Assert.All(slotStates, state =>
        {
            Assert.Equal(ResourceReplicaSlotRuntimeStatus.Materialized, state.Status);
            Assert.Equal(result.Revision.ServiceId, state.ServiceId);
            Assert.Equal(result.Revision.ReplicaGroup?.Id, state.ReplicaGroupId);
            Assert.Equal(result.Revision.ReplicaGroup?.RuntimeRevisionId, state.RuntimeRevisionId);
            Assert.Equal("tests", state.TriggeredBy);
            Assert.Equal(0, state.AttemptCount);
            Assert.NotNull(state.LastCompletedAt);
            Assert.Contains("Deployment materialized replica slot", state.LastResult, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task ApplyDeploymentAsync_RemovesStaleReplicaSlotStatesWhenScalingIn()
    {
        var resource = CreateResource();
        var provider = new RecordingServiceProcedureProvider(resource);
        var deploymentStore = new InMemoryResourceOrchestratorDeploymentStore();
        var reconciliationStore = new InMemoryResourceReplicaGroupReconciliationStore();
        var deployments = CreateDeployments(
            resource,
            provider,
            deploymentStore: deploymentStore,
            reconciliationStore: reconciliationStore);
        await deployments.ApplyDeploymentAsync(
            resource,
            CreateDeployment(resource.Id, "default", replicas: 4),
            triggeredBy: "tests");

        await deployments.ApplyDeploymentAsync(
            resource,
            CreateDeployment(resource.Id, "default", replicas: 2),
            triggeredBy: "tests");

        var slotStates = reconciliationStore.ListRuntimeStates(resource.Id);
        Assert.Equal([1, 2], slotStates.Select(state => state.SlotOrdinal).ToArray());
        Assert.All(slotStates, state =>
        {
            Assert.Equal(ResourceReplicaSlotRuntimeStatus.Materialized, state.Status);
            Assert.Equal("cloudshell-application-api-rev-2-replicas", state.ReplicaGroupId);
        });
    }

    [Fact]
    public async Task ApplyDeploymentAsync_DoesNotRetirePreviousReplicaGroupWhenAllPreviousSlotsAreRetained()
    {
        var resource = CreateResource();
        var provider = new RecordingServiceProcedureProvider(resource);
        var deploymentStore = new InMemoryResourceOrchestratorDeploymentStore();
        var deployments = CreateDeployments(resource, provider, deploymentStore: deploymentStore);
        var firstDeployment = CreateDeployment(resource.Id, "default", replicas: 3);
        var replacementPolicy = ResourceOrchestratorReplicaGroupReconciliationPolicy.Default with
        {
            RetainPreviousReplicaSlots = 3
        };
        var secondDeployment = WithReconciliationPolicy(
            CreateDeployment(resource.Id, "default", replicas: 3) with
            {
                RevisionId = "rev-3"
            },
            replacementPolicy);

        await deployments.ApplyDeploymentAsync(resource, firstDeployment);
        var secondResult = await deployments.ApplyDeploymentAsync(resource, secondDeployment);

        Assert.Empty(secondResult.ReplicaGroupsToTearDown);
    }

    [Fact]
    public async Task ApplyDeploymentAsync_RecordsFailedDeployment()
    {
        var resource = CreateResource();
        var provider = new RecordingServiceProcedureProvider(resource, failOnStart: true);
        var resourceEvents = new InMemoryResourceEventStore();
        var deploymentStore = new InMemoryResourceOrchestratorDeploymentStore();
        var deployments = CreateDeployments(resource, provider, resourceEvents, deploymentStore);
        var deployment = CreateDeployment(resource.Id, "default", replicas: 1);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            deployments.ApplyDeploymentAsync(
                resource,
                deployment,
                triggeredBy: "tests",
                cause: "Container app deployment."));

        Assert.Equal("Replica execution failed.", exception.Message);
        var deploymentRecord = Assert.Single(deploymentStore.List(new ResourceOrchestratorDeploymentQuery(
            SourceResourceId: resource.Id,
            DeploymentId: deployment.Id)));
        Assert.Equal(ResourceOrchestratorDeploymentStatus.Failed, deploymentRecord.Status);
        Assert.Null(deploymentRecord.Revision);
        Assert.Null(deploymentRecord.ReplicaGroup);
        Assert.Equal("tests", deploymentRecord.TriggeredBy);
        Assert.Equal("Container app deployment.", deploymentRecord.Cause);
        Assert.Equal("Replica execution failed.", deploymentRecord.Error);
        Assert.NotNull(deploymentRecord.CompletedAt);
        Assert.Equal(
            [ResourceActionKind.Stop],
            provider.ExecutedActions.Select(action => action.Kind).ToArray());
        Assert.Equal(
            ["cloudshell-application-api-rev-2"],
            provider.ExecutedInstances.Select(instance => instance.Instance.Name).ToArray());
        var events = resourceEvents
            .GetEvents(new ResourceEventQuery(ResourceId: resource.Id))
            .Reverse()
            .ToArray();
        Assert.Equal(
            [
                ResourceEventTypes.Events.Deployment.Applying,
                ResourceEventTypes.Events.Deployment.ServiceReconciling,
                ResourceEventTypes.Events.Deployment.ServiceReconciled,
                ResourceEventTypes.Events.Deployment.ReplicaMaterializing,
                ResourceEventTypes.Events.Deployment.RollingBack,
                ResourceEventTypes.Events.Deployment.RolledBack,
                ResourceEventTypes.Events.Deployment.Failed
            ],
            events.Select(resourceEvent => resourceEvent.EventType).ToArray());
        Assert.Contains(
            events,
            resourceEvent =>
                resourceEvent.EventType == ResourceEventTypes.Events.Deployment.RollingBack &&
                resourceEvent.Severity == ResourceSignalSeverity.Warning);
        Assert.Contains(
            events,
            resourceEvent =>
                resourceEvent.EventType == ResourceEventTypes.Events.Deployment.Failed &&
                resourceEvent.Severity == ResourceSignalSeverity.Error);
    }

    [Fact]
    public async Task ApplyDeploymentAsync_IncrementsOrchestratorRevisionNumberForSameService()
    {
        var resource = CreateResource();
        var provider = new RecordingServiceProcedureProvider(resource);
        var deploymentStore = new InMemoryResourceOrchestratorDeploymentStore();
        var deployments = CreateDeployments(resource, provider, deploymentStore: deploymentStore);
        var firstDeployment = CreateDeployment(resource.Id, "default", replicas: 1);
        var secondDeployment = firstDeployment with
        {
            Id = "custom-deployment",
            RevisionId = "rev-3"
        };

        var firstResult = await deployments.ApplyDeploymentAsync(resource, firstDeployment);
        var secondResult = await deployments.ApplyDeploymentAsync(resource, secondDeployment);

        Assert.Equal(1, firstResult.Revision.RevisionNumber);
        Assert.Equal(2, secondResult.Revision.RevisionNumber);
        Assert.Null(firstResult.Deployment.BasedOnRevisionId);
        Assert.Null(firstResult.Revision.BasedOnRevisionId);
        Assert.Null(firstResult.Revision.ProvisionedBy);
        Assert.Equal(firstResult.Revision.Id, secondResult.Deployment.BasedOnRevisionId);
        Assert.Equal(firstResult.Revision.Id, secondResult.Revision.BasedOnRevisionId);
        Assert.Null(secondResult.Revision.ProvisionedBy);
        Assert.True(firstResult.Revision.CreatedAt <= secondResult.Revision.CreatedAt);
        var records = deploymentStore.List(new ResourceOrchestratorDeploymentQuery(
            SourceResourceId: resource.Id));
        Assert.Equal(2, records.Count);
        Assert.Equal(
            [
                firstResult.Revision.Id,
                secondResult.Revision.Id
            ],
            records
                .Select(record => record.Revision)
                .Where(revision => revision is not null)
                .OrderBy(revision => revision!.RevisionNumber)
                .Select(revision => revision!.Id)
                .ToArray());
        Assert.All(
            records.Select(record => record.Revision).Where(revision => revision is not null),
            revision => Assert.StartsWith("env-", revision!.Id.ToString(), StringComparison.Ordinal));
        Assert.Contains(records, record =>
            record.RevisionId == "rev-2" &&
            record.Revision?.RevisionNumber == 1);
        Assert.Contains(records, record =>
            record.RevisionId == "rev-3" &&
            record.Revision?.RevisionNumber == 2 &&
            record.Deployment.BasedOnRevisionId == firstResult.Revision.Id &&
            record.Revision.BasedOnRevisionId == firstResult.Revision.Id);
    }

    [Fact]
    public async Task ApplyDeploymentAsync_PreservesExplicitBasedOnRevisionId()
    {
        var resource = CreateResource();
        var provider = new RecordingServiceProcedureProvider(resource);
        var deploymentStore = new InMemoryResourceOrchestratorDeploymentStore();
        var deployments = CreateDeployments(resource, provider, deploymentStore: deploymentStore);
        var firstDeployment = CreateDeployment(resource.Id, "default", replicas: 1);
        var secondDeployment = firstDeployment with { RevisionId = "rev-3" };
        var restoreDeployment = firstDeployment with
        {
            RevisionId = "rev-4"
        };

        var firstResult = await deployments.ApplyDeploymentAsync(resource, firstDeployment);
        await deployments.ApplyDeploymentAsync(resource, secondDeployment);
        var basedOnEnvironmentRevisionId = firstResult.Revision.Id;
        restoreDeployment = restoreDeployment with
        {
            BasedOnRevisionId = basedOnEnvironmentRevisionId
        };

        var restoreResult = await deployments.ApplyDeploymentAsync(resource, restoreDeployment);

        Assert.Equal(basedOnEnvironmentRevisionId, restoreResult.Deployment.BasedOnRevisionId);
        Assert.Equal(basedOnEnvironmentRevisionId, restoreResult.Revision.BasedOnRevisionId);
        Assert.Equal("rev-4", restoreResult.Deployment.RevisionId);
        Assert.NotEqual("rev-4", restoreResult.Revision.Id.ToString());
        var restoreRecord = Assert.Single(
            deploymentStore.List(new ResourceOrchestratorDeploymentQuery(
                SourceResourceId: resource.Id,
                DeploymentId: firstDeployment.Id)),
            record => string.Equals(record.RevisionId, "rev-4", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(basedOnEnvironmentRevisionId, restoreRecord.Deployment.BasedOnRevisionId);
        Assert.Equal(basedOnEnvironmentRevisionId, restoreRecord.Revision?.BasedOnRevisionId);
    }

    [Fact]
    public async Task ApplyDeploymentAsync_AllowsDifferentResourcesToApplyConcurrently()
    {
        var api = CreateResource("application:api", "API");
        var worker = CreateResource("application:worker", "Worker");
        var gate = new ConcurrentDeploymentGate(expectedCount: 2);
        var provider = new RecordingServiceProcedureProvider([api, worker], gate);
        var deploymentStore = new InMemoryResourceOrchestratorDeploymentStore();
        var deployments = CreateDeployments([api, worker], provider, deploymentStore: deploymentStore);

        var apiApply = deployments.ApplyDeploymentAsync(
            api,
            CreateDeployment(api.Id, "default", replicas: 1));
        var workerApply = deployments.ApplyDeploymentAsync(
            worker,
            CreateDeployment(worker.Id, "default", replicas: 1));

        var results = await Task.WhenAll(apiApply, workerApply)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.All(
            results,
            result => Assert.Equal(ResourceOrchestratorDeploymentStatus.Active, result.Deployment.Status));
        Assert.Equal(
            [api.Id, worker.Id],
            provider.PreparedServices
                .Select(service => service.ResourceId)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray());
        Assert.Equal(
            [api.Id, worker.Id],
            deploymentStore.List()
                .Select(record => record.SourceResourceId)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    [Fact]
    public async Task ApplyDeploymentAsync_SerializesDeploymentsForSameResource()
    {
        var resource = CreateResource();
        var probe = new DeploymentConcurrencyProbe(TimeSpan.FromMilliseconds(100));
        var provider = new RecordingServiceProcedureProvider([resource], concurrencyProbe: probe);
        var deploymentStore = new InMemoryResourceOrchestratorDeploymentStore();
        var deployments = CreateDeployments(resource, provider, deploymentStore: deploymentStore);
        var firstDeployment = CreateDeployment(resource.Id, "default", replicas: 1);
        var secondDeployment = CreateDeployment(resource.Id, "default", replicas: 1) with
        {
            RevisionId = "rev-3"
        };

        var firstApply = deployments.ApplyDeploymentAsync(resource, firstDeployment);
        await probe.FirstEntered.WaitAsync(TimeSpan.FromSeconds(5));
        var secondApply = deployments.ApplyDeploymentAsync(resource, secondDeployment);
        var results = await Task.WhenAll(firstApply, secondApply)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, probe.MaxConcurrentPreparations);
        Assert.Null(results[0].Deployment.BasedOnRevisionId);
        Assert.Equal(results[0].Revision.Id, results[1].Deployment.BasedOnRevisionId);
        Assert.Equal(results[0].Revision.Id, results[1].Revision.BasedOnRevisionId);
        Assert.Equal([1, 2], results.Select(result => result.Revision.RevisionNumber).ToArray());
        Assert.Equal(
            ["rev-2", "rev-3"],
            results.Select(result => result.Deployment.RevisionId).ToArray());
    }

    [Fact]
    public async Task TearDownServiceAsync_TearsDownProvidedServiceSpec()
    {
        var resource = CreateResource();
        var provider = new RecordingServiceProcedureProvider(resource);
        var orchestration = CreateOrchestration(resource, provider);
        var service = CreateDeployment(resource.Id, "default", replicas: 2).Spec.Service with
        {
            RuntimeRevisionId = "rev-2"
        };

        var result = await orchestration.TearDownServiceAsync(
            resource,
            service,
            triggeredBy: "tests",
            cause: "Container app service cleanup.");

        Assert.Equal($"Tore down service '{service.Name}' for {resource.Name}.", result.Message);
        var preparedContext = Assert.Single(provider.PreparedContexts);
        Assert.Equal(ResourceActionKind.Stop, Assert.Single(provider.PreparedActions).Kind);
        Assert.Equal(service.Name, preparedContext.Service.Name);
        Assert.Equal("cloudshell-application-api-rev-2-replicas", preparedContext.ReplicaGroup?.Id);
        Assert.Equal(
            [
                "cloudshell-application-api-rev-2-replica-1",
                "cloudshell-application-api-rev-2-replica-2"
            ],
            provider.ExecutedInstances
                .Select(instance => instance.Instance.Name)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray());
        Assert.All(
            provider.ExecutedActions,
            action => Assert.Equal(ResourceActionKind.Stop, action.Kind));
    }

    [Fact]
    public async Task TearDownServiceAsync_RejectsServiceForDifferentResource()
    {
        var resource = CreateResource();
        var provider = new RecordingServiceProcedureProvider(resource);
        var orchestration = CreateOrchestration(resource, provider);
        var service = CreateDeployment("application:worker", "default", replicas: 1).Spec.Service;

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            orchestration.TearDownServiceAsync(resource, service));

        Assert.Equal(ControlPlaneErrorCodes.InvalidRequest, exception.Error.Code);
        Assert.Contains(
            $"Service '{service.Name}' belongs to resource 'application:worker', not '{resource.Id}'",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Empty(provider.PreparedContexts);
        Assert.Empty(provider.ExecutedInstances);
    }

    [Fact]
    public async Task TearDownReplicaGroupAsync_TearsDownProvidedReplicaGroupWithoutServicePrepare()
    {
        var resource = CreateResource();
        var provider = new RecordingServiceProcedureProvider(resource);
        var orchestration = CreateOrchestration(resource, provider);
        var service = CreateDeployment(resource.Id, "default", replicas: 3).Spec.Service with
        {
            RuntimeRevisionId = "rev-2"
        };
        var replicaGroup = ResourceOrchestratorReplicaGroups.CreateDefaultReplicaGroup(service);

        var result = await orchestration.TearDownReplicaGroupAsync(
            resource,
            service,
            replicaGroup,
            triggeredBy: "tests",
            cause: "Container app replica group cleanup.");

        Assert.Equal($"Tore down replica group '{replicaGroup.Id}' for service '{service.Name}'.", result.Message);
        Assert.Empty(provider.PreparedContexts);
        Assert.Equal(
            [
                "cloudshell-application-api-rev-2-replica-1",
                "cloudshell-application-api-rev-2-replica-2",
                "cloudshell-application-api-rev-2-replica-3"
            ],
            provider.ExecutedInstances
                .Select(instance => instance.Instance.Name)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray());
        Assert.All(
            provider.ExecutedActions,
            action => Assert.Equal(ResourceActionKind.Stop, action.Kind));
        Assert.All(
            provider.ExecutedInstances,
            instance => Assert.Equal(replicaGroup, instance.ReplicaGroup));
    }

    [Fact]
    public async Task ReconcileReplicaSlotAsync_ReplacesFailedSlotOccupant()
    {
        var resource = CreateResource();
        var resourceEvents = new InMemoryResourceEventStore();
        var service = CreateDeployment(resource.Id, "default", replicas: 3).Spec.Service;
        var provider = new RecordingServiceProcedureProvider(resource)
        {
            OrchestratorService = service
        };
        var orchestration = CreateOrchestration(resource, provider, resourceEvents);

        var result = await orchestration.ReconcileReplicaSlotAsync(
            resource,
            2,
            "Connection refused.",
            triggeredBy: "tests");

        Assert.Equal(
            "Replaced replica group 'cloudshell-application-api-replicas' slot 2/3 occupant 'cloudshell-application-api-replica-2'.",
            result.Message);
        Assert.Equal(
            [
                "Stop:cloudshell-application-api-replica-2",
                "Start:cloudshell-application-api-replica-2"
            ],
            provider.ExecutedInstanceActions.ToArray());
        Assert.Empty(provider.PreparedActions);
        var events = resourceEvents
            .GetEvents(new ResourceEventQuery(ResourceId: resource.Id))
            .Reverse()
            .ToArray();
        Assert.Equal(
            [
                ResourceEventTypes.Events.ReplicaManagement.SlotUnhealthy,
                ResourceEventTypes.Events.ReplicaManagement.OccupantCrashed,
                ResourceEventTypes.Events.ReplicaManagement.ReplacementScheduled,
                ResourceEventTypes.Events.ReplicaManagement.ReplacementMaterializing,
                ResourceEventTypes.Events.ReplicaManagement.ReplacementMaterialized
            ],
            events.Select(resourceEvent => resourceEvent.EventType).ToArray());
        Assert.All(
            events,
            resourceEvent => Assert.Equal("tests", resourceEvent.TriggeredBy));
        Assert.Contains(
            events,
            resourceEvent =>
                resourceEvent.EventType == ResourceEventTypes.Events.ReplicaManagement.SlotUnhealthy &&
                resourceEvent.Message.Contains("Connection refused.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReconcileReplicaSlotAsync_UsesActiveMaterializedReplicaGroup()
    {
        var resource = CreateResource();
        var deploymentProvider = new RecordingServiceProcedureProvider(resource);
        var deploymentStore = new InMemoryResourceOrchestratorDeploymentStore();
        var deployments = CreateDeployments(resource, deploymentProvider, deploymentStore: deploymentStore);

        var result = await deployments.ApplyDeploymentAsync(
            resource,
            CreateDeployment(resource.Id, "default", replicas: 3),
            triggeredBy: "tests");
        var repairProvider = new RecordingServiceProcedureProvider(resource);
        var orchestration = CreateOrchestration(
            resource,
            repairProvider,
            deploymentStore: deploymentStore);

        await orchestration.ReconcileReplicaSlotAsync(
            resource,
            2,
            "Container exited.",
            triggeredBy: "replica-management");

        Assert.Equal("cloudshell-application-api-rev-2-replicas", result.Revision.ReplicaGroup?.Id);
        Assert.Equal(
            [
                "Stop:cloudshell-application-api-rev-2-replica-2",
                "Start:cloudshell-application-api-rev-2-replica-2"
            ],
            repairProvider.ExecutedInstanceActions.ToArray());
        var stopContext = repairProvider.ExecutedInstances.First(context =>
            context.Instance.ReplicaOrdinal == 2 &&
            context.Instance.RuntimeRevisionId == "rev-2");
        Assert.Equal(result.Revision.ReplicaGroup, stopContext.ReplicaGroup);
        Assert.Equal("rev-2", stopContext.Service.RuntimeRevisionId);
    }

    [Fact]
    public async Task ReplicaGroupReconciliationService_TracksSuccessfulSlotRepairState()
    {
        var resource = CreateResource();
        var service = CreateDeployment(resource.Id, "default", replicas: 3).Spec.Service;
        var provider = new RecordingServiceProcedureProvider(resource)
        {
            OrchestratorService = service
        };
        var (reconciliation, store) = CreateReplicaGroupReconciliation(resource, provider);

        reconciliation.ObserveUnhealthyReplicaSlot(
            resource,
            2,
            "Connection refused.",
            "tests");

        var observed = AssertReplicaSlotState(
            store,
            resource.Id,
            2,
            ResourceReplicaSlotRuntimeStatus.Unhealthy);
        Assert.Equal("Connection refused.", observed.Detail);
        Assert.Equal("tests", observed.TriggeredBy);
        Assert.Equal(0, observed.AttemptCount);

        await reconciliation.ProcessPendingAsync();

        var repaired = AssertReplicaSlotState(
            store,
            resource.Id,
            2,
            ResourceReplicaSlotRuntimeStatus.Repaired);
        Assert.Equal(1, repaired.AttemptCount);
        Assert.NotNull(repaired.LastAttemptedAt);
        Assert.NotNull(repaired.LastCompletedAt);
        Assert.Contains("Replaced replica group", repaired.LastResult, StringComparison.Ordinal);
        Assert.Empty(store.DequeuePending(1));
    }

    [Fact]
    public async Task ReplicaGroupReconciliationService_WaitsUntilFailureThresholdBeforeRepairingSlot()
    {
        var resource = CreateResource();
        var deploymentStore = new InMemoryResourceOrchestratorDeploymentStore();
        var reconciliationStore = new InMemoryResourceReplicaGroupReconciliationStore();
        var deploymentProvider = new RecordingServiceProcedureProvider(resource);
        var deployments = CreateDeployments(
            resource,
            deploymentProvider,
            deploymentStore: deploymentStore,
            reconciliationStore: reconciliationStore);
        await deployments.ApplyDeploymentAsync(
            resource,
            WithReplicaManagementPolicy(
                CreateDeployment(resource.Id, "default", replicas: 3),
                new ResourceOrchestratorReplicaManagementPolicy(FailureThreshold: 2)),
            triggeredBy: "tests");
        var repairProvider = new RecordingServiceProcedureProvider(resource);
        var (reconciliation, store) = CreateReplicaGroupReconciliation(
            resource,
            repairProvider,
            deploymentStore: deploymentStore,
            reconciliationStore: reconciliationStore);

        reconciliation.ObserveUnhealthyReplicaSlot(
            resource,
            2,
            "First failed observation.",
            "tests");
        await reconciliation.ProcessPendingAsync();

        var observed = AssertReplicaSlotState(
            store,
            resource.Id,
            2,
            ResourceReplicaSlotRuntimeStatus.Unhealthy);
        Assert.Equal(0, observed.AttemptCount);
        Assert.Equal(1, observed.ObservationCount);
        Assert.Contains("1/2 unhealthy observations", observed.LastResult, StringComparison.Ordinal);
        Assert.Empty(repairProvider.ExecutedInstanceActions);

        reconciliation.ObserveUnhealthyReplicaSlot(
            resource,
            2,
            "Second failed observation.",
            "tests");
        await reconciliation.ProcessPendingAsync();

        var repaired = AssertReplicaSlotState(
            store,
            resource.Id,
            2,
            ResourceReplicaSlotRuntimeStatus.Repaired);
        Assert.Equal(1, repaired.AttemptCount);
        Assert.Equal(0, repaired.ObservationCount);
        Assert.Equal(
            [
                "Stop:cloudshell-application-api-rev-2-replica-2",
                "Start:cloudshell-application-api-rev-2-replica-2"
            ],
            repairProvider.ExecutedInstanceActions.ToArray());
    }

    [Fact]
    public async Task ReplicaGroupReconciliationService_StampsActiveReplicaGroupOnSlotRepairState()
    {
        var resource = CreateResource();
        var deploymentStore = new InMemoryResourceOrchestratorDeploymentStore();
        var deploymentProvider = new RecordingServiceProcedureProvider(resource);
        var deployments = CreateDeployments(
            resource,
            deploymentProvider,
            deploymentStore: deploymentStore);
        var deploymentResult = await deployments.ApplyDeploymentAsync(
            resource,
            CreateDeployment(resource.Id, "default", replicas: 3),
            triggeredBy: "tests");
        var repairProvider = new RecordingServiceProcedureProvider(resource);
        var (reconciliation, store) = CreateReplicaGroupReconciliation(
            resource,
            repairProvider,
            deploymentStore: deploymentStore);

        reconciliation.ObserveUnhealthyReplicaSlot(
            resource,
            2,
            "Container exited.",
            "tests");
        await reconciliation.ProcessPendingAsync();

        var repaired = AssertReplicaSlotState(
            store,
            resource.Id,
            2,
            ResourceReplicaSlotRuntimeStatus.Repaired);
        Assert.Equal(deploymentResult.Revision.ServiceId, repaired.ServiceId);
        Assert.Equal(deploymentResult.Revision.ReplicaGroup?.Id, repaired.ReplicaGroupId);
        Assert.Equal(deploymentResult.Revision.ReplicaGroup?.RuntimeRevisionId, repaired.RuntimeRevisionId);
    }

    [Fact]
    public async Task ReplicaGroupReconciliationService_TracksFailedSlotRepairState()
    {
        var resource = CreateResource();
        var service = CreateDeployment(resource.Id, "default", replicas: 3).Spec.Service;
        var provider = new RecordingServiceProcedureProvider(resource, failOnStart: true)
        {
            OrchestratorService = service
        };
        var resourceEvents = new InMemoryResourceEventStore();
        var (reconciliation, store) = CreateReplicaGroupReconciliation(
            resource,
            provider,
            resourceEvents);

        reconciliation.ObserveUnhealthyReplicaSlot(
            resource,
            2,
            "Container exited.",
            "tests");

        await reconciliation.ProcessPendingAsync();

        var failed = AssertReplicaSlotState(
            store,
            resource.Id,
            2,
            ResourceReplicaSlotRuntimeStatus.RepairFailed);
        Assert.Equal(1, failed.AttemptCount);
        Assert.NotNull(failed.LastAttemptedAt);
        Assert.NotNull(failed.LastCompletedAt);
        Assert.Equal("Replica execution failed.", failed.LastResult);
        var failureEvents = resourceEvents.GetEvents(new ResourceEventQuery(
            ResourceId: resource.Id,
            EventType: ResourceEventTypes.Events.ReplicaManagement.ReconciliationFailed));
        Assert.Contains(
            failureEvents,
            resourceEvent => resourceEvent.Message.Contains(
                "Replica slot 2 reconciliation failed: Replica execution failed.",
                StringComparison.Ordinal));
        Assert.Contains(
            failureEvents,
            resourceEvent => resourceEvent.Message.Contains(
                "replacement failed: Replica execution failed.",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReplicaGroupReconciliationService_DoesNotRepairAfterMaxAttempts()
    {
        var resource = CreateResource();
        var deploymentStore = new InMemoryResourceOrchestratorDeploymentStore();
        var reconciliationStore = new InMemoryResourceReplicaGroupReconciliationStore();
        var deploymentProvider = new RecordingServiceProcedureProvider(resource);
        var deployments = CreateDeployments(
            resource,
            deploymentProvider,
            deploymentStore: deploymentStore,
            reconciliationStore: reconciliationStore);
        await deployments.ApplyDeploymentAsync(
            resource,
            WithReplicaManagementPolicy(
                CreateDeployment(resource.Id, "default", replicas: 3),
                new ResourceOrchestratorReplicaManagementPolicy(MaxAttempts: 1)),
            triggeredBy: "tests");
        var repairProvider = new RecordingServiceProcedureProvider(resource, failOnStart: true);
        var resourceEvents = new InMemoryResourceEventStore();
        var (reconciliation, store) = CreateReplicaGroupReconciliation(
            resource,
            repairProvider,
            resourceEvents,
            deploymentStore,
            reconciliationStore);

        reconciliation.ObserveUnhealthyReplicaSlot(
            resource,
            2,
            "Container exited.",
            "tests");
        await reconciliation.ProcessPendingAsync();

        var failed = AssertReplicaSlotState(
            store,
            resource.Id,
            2,
            ResourceReplicaSlotRuntimeStatus.RepairFailed);
        Assert.Equal(1, failed.AttemptCount);
        Assert.Equal(["Stop:cloudshell-application-api-rev-2-replica-2"], repairProvider.ExecutedInstanceActions.ToArray());

        reconciliation.ObserveUnhealthyReplicaSlot(
            resource,
            2,
            "Container exited again.",
            "tests");
        await reconciliation.ProcessPendingAsync();

        var exhausted = AssertReplicaSlotState(
            store,
            resource.Id,
            2,
            ResourceReplicaSlotRuntimeStatus.RepairFailed);
        Assert.Equal(1, exhausted.AttemptCount);
        Assert.Contains("repair exhausted", exhausted.LastResult, StringComparison.Ordinal);
        Assert.Equal(["Stop:cloudshell-application-api-rev-2-replica-2"], repairProvider.ExecutedInstanceActions.ToArray());
        Assert.Contains(
            resourceEvents.GetEvents(new ResourceEventQuery(
                ResourceId: resource.Id,
                EventType: ResourceEventTypes.Events.ReplicaManagement.ReconciliationExhausted)),
            resourceEvent => resourceEvent.Message.Contains("repair exhausted", StringComparison.Ordinal));
    }

    private static ResourceDeploymentService CreateDeployments(
        Resource resource,
        RecordingServiceProcedureProvider provider,
        IResourceEventSink? resourceEvents = null,
        IResourceOrchestratorDeploymentStore? deploymentStore = null,
        IReadOnlyList<IResourceOrchestrator>? orchestrators = null,
        IResourceReplicaGroupReconciliationStore? reconciliationStore = null) =>
        CreateDeployments([resource], provider, resourceEvents, deploymentStore, orchestrators, reconciliationStore);

    private static ResourceDeploymentService CreateDeployments(
        IReadOnlyList<Resource> resources,
        RecordingServiceProcedureProvider provider,
        IResourceEventSink? resourceEvents = null,
        IResourceOrchestratorDeploymentStore? deploymentStore = null,
        IReadOnlyList<IResourceOrchestrator>? orchestrators = null,
        IResourceReplicaGroupReconciliationStore? reconciliationStore = null)
    {
        var registrations = new TestResourceRegistrationStore(
            resources.Select(resource =>
                new ResourceRegistration(resource.Id, provider.Id, null, DateTimeOffset.UtcNow, resource.DependsOn)));
        return new ResourceDeploymentService(
            orchestrators ?? [new DefaultResourceOrchestrator()],
            [new DefaultResourceDeploymentService(deploymentStore, reconciliationStore)],
            new TestResourceManagerStore(resources, provider),
            registrations,
            CreateSelectionStore(),
            resourceEvents: resourceEvents,
            deploymentStore: deploymentStore);
    }

    private static (ResourceReplicaGroupReconciliationService Service, InMemoryResourceReplicaGroupReconciliationStore Store)
        CreateReplicaGroupReconciliation(
            Resource resource,
            RecordingServiceProcedureProvider provider,
            IResourceEventSink? resourceEvents = null,
            IResourceOrchestratorDeploymentStore? deploymentStore = null,
            InMemoryResourceReplicaGroupReconciliationStore? reconciliationStore = null)
    {
        var resourceManager = new TestResourceManagerStore([resource], provider);
        var registrations = new TestResourceRegistrationStore(
        [
            new ResourceRegistration(resource.Id, provider.Id, null, DateTimeOffset.UtcNow, resource.DependsOn)
        ]);
        var orchestration = new ResourceOrchestrationService(
            [new DefaultResourceOrchestrator()],
            [],
            resourceManager,
            registrations,
            new ResourceDeclarationStore(),
            CreateSelectionStore(),
            resourceEvents: resourceEvents,
            deploymentStore: deploymentStore);
        var store = reconciliationStore ?? new InMemoryResourceReplicaGroupReconciliationStore();
        return (
            new ResourceReplicaGroupReconciliationService(
                orchestration,
                resourceManager,
                store,
                deploymentStore,
                resourceEvents),
            store);
    }

    private static ResourceReplicaSlotRuntimeState AssertReplicaSlotState(
        IResourceReplicaGroupReconciliationStore store,
        string resourceId,
        int slotOrdinal,
        ResourceReplicaSlotRuntimeStatus status)
    {
        var state = store.GetRuntimeState(resourceId, slotOrdinal);
        Assert.NotNull(state);
        Assert.Equal(status, state.Status);
        Assert.Equal(resourceId, state.ResourceId);
        Assert.Equal(slotOrdinal, state.SlotOrdinal);
        return state;
    }

    private static ResourceOrchestrationService CreateOrchestration(
        Resource resource,
        RecordingServiceProcedureProvider provider,
        IResourceEventSink? resourceEvents = null,
        IResourceOrchestratorDeploymentStore? deploymentStore = null) =>
        CreateOrchestration([resource], provider, resourceEvents, deploymentStore);

    private static ResourceOrchestrationService CreateOrchestration(
        IReadOnlyList<Resource> resources,
        RecordingServiceProcedureProvider provider,
        IResourceEventSink? resourceEvents = null,
        IResourceOrchestratorDeploymentStore? deploymentStore = null)
    {
        var registrations = new TestResourceRegistrationStore(
            resources.Select(resource =>
                new ResourceRegistration(resource.Id, provider.Id, null, DateTimeOffset.UtcNow, resource.DependsOn)));
        return new ResourceOrchestrationService(
            [new DefaultResourceOrchestrator()],
            [],
            new TestResourceManagerStore(resources, provider),
            registrations,
            new ResourceDeclarationStore(),
            CreateSelectionStore(),
            resourceEvents: resourceEvents,
            deploymentStore: deploymentStore);
    }

    private static Resource CreateResource(
        string id = "application:api",
        string name = "API",
        ResourceState state = ResourceState.Running) =>
        new(
            id,
            name,
            "container-app",
            "Container App",
            "local",
            state,
            [],
            "1",
            DateTimeOffset.UtcNow,
            [],
            TypeId: "container-app",
            Actions: [ResourceAction.Start, ResourceAction.Stop]);

    private static ResourceOrchestratorDeployment CreateDeployment(
        string resourceId,
        string orchestratorId,
        int replicas)
    {
        var serviceName = $"cloudshell-{resourceId.Replace(':', '-').Replace('_', '-').ToLowerInvariant()}";
        var service = new ResourceOrchestratorService(
            resourceId,
            serviceName,
            new ResourceWorkloadConfiguration(
                ResourceWorkloadKind.ContainerImage,
                "api",
                Image: "ghcr.io/example/api:2",
                Replicas: replicas,
                ReplicasEnabled: replicas > 1),
            Ports: [new ServicePort("http", 8080, Protocol: "http")],
            Networks: ["cloudshell"]);
        return new ResourceOrchestratorDeployment(
            $"{serviceName}-deployment",
            orchestratorId,
            resourceId,
            service.Name,
            "rev-2",
            new ResourceOrchestratorDeploymentSpec(service, "rev-2"),
            ResourceOrchestratorDeploymentStatus.Pending);
    }

    private static ResourceOrchestratorDeployment WithReconciliationPolicy(
        ResourceOrchestratorDeployment deployment,
        ResourceOrchestratorReplicaGroupReconciliationPolicy policy)
    {
        var service = deployment.Spec.Service with
        {
            RuntimeRevisionId = deployment.RevisionId
        };
        var replicaGroup = ResourceOrchestratorReplicaGroupDefinition
            .FromReplicaGroup(
                ResourceOrchestratorReplicaGroups.CreateDefaultReplicaGroup(service),
                deployment.Spec.WorkloadVersion)
            with
            {
                ReconciliationPolicy = policy
            };
        return deployment with
        {
            Spec = deployment.Spec with
            {
                Definition = new ResourceOrchestratorDeploymentDefinition(
                    ResourceOrchestratorDeploymentDefinition.CurrentDefinitionVersion,
                    Services:
                    [
                        new ResourceOrchestratorServiceDefinition(
                            service.Name,
                            ResourceOrchestratorDeploymentDefinitionTypes.Service,
                            ResourceOrchestratorDeploymentDefinition.CurrentDefinitionVersion,
                            Resources: [replicaGroup.ToResourceDefinition()])
                    ])
            }
        };
    }

    private static ResourceOrchestratorDeployment WithReplicaManagementPolicy(
        ResourceOrchestratorDeployment deployment,
        ResourceOrchestratorReplicaManagementPolicy policy)
    {
        var service = deployment.Spec.Service with
        {
            RuntimeRevisionId = deployment.RevisionId
        };
        var replicaGroup = ResourceOrchestratorReplicaGroupDefinition
            .FromReplicaGroup(
                ResourceOrchestratorReplicaGroups.CreateDefaultReplicaGroup(service),
                deployment.Spec.WorkloadVersion)
            with
            {
                ManagementPolicy = policy
            };
        return deployment with
        {
            Spec = deployment.Spec with
            {
                Definition = new ResourceOrchestratorDeploymentDefinition(
                    ResourceOrchestratorDeploymentDefinition.CurrentDefinitionVersion,
                    Services:
                    [
                        new ResourceOrchestratorServiceDefinition(
                            service.Name,
                            ResourceOrchestratorDeploymentDefinitionTypes.Service,
                            ResourceOrchestratorDeploymentDefinition.CurrentDefinitionVersion,
                            Resources: [replicaGroup.ToResourceDefinition()])
                    ])
            }
        };
    }

    private static ResourceOrchestratorSelectionStore CreateSelectionStore() =>
        new(
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))),
            new TestOptionsMonitor<ResourceManagerOptions>(new ResourceManagerOptions()));

    private sealed class RecordingServiceProcedureProvider(
        IReadOnlyList<Resource> resources,
        ConcurrentDeploymentGate? gate = null,
        bool failOnStart = false,
        DeploymentConcurrencyProbe? concurrencyProbe = null) :
        IResourceProvider,
        IResourceOrchestratorServiceProcedureProvider
    {
        public RecordingServiceProcedureProvider(Resource resource)
            : this([resource])
        {
        }

        public RecordingServiceProcedureProvider(Resource resource, bool failOnStart)
            : this([resource], failOnStart: failOnStart)
        {
        }

        public string Id => "applications.container-app";

        public string DisplayName => "Container App";

        public ConcurrentBag<ResourceOrchestratorService> PreparedServices { get; } = [];

        public ConcurrentBag<ResourceOrchestratorServiceProcedureContext> PreparedContexts { get; } = [];

        public ConcurrentBag<ResourceAction> PreparedActions { get; } = [];

        public ConcurrentBag<ResourceOrchestratorServiceProcedureContext> RoutedContexts { get; } = [];

        public ConcurrentBag<ResourceOrchestratorServiceInstanceContext> ExecutedInstances { get; } = [];

        public ConcurrentBag<ResourceAction> ExecutedActions { get; } = [];

        public ConcurrentQueue<string> ExecutedInstanceActions { get; } = [];

        public ConcurrentQueue<string> Operations { get; } = [];

        public ResourceOrchestratorService? OrchestratorService { get; init; }

        public IReadOnlyList<Resource> GetResources() => resources;

        public bool CanExecuteOrchestratorService(
            Resource resource,
            ResourceAction action) =>
            action.Kind is ResourceActionKind.Start or ResourceActionKind.Stop;

        public Task<ResourceOrchestratorService> CreateOrchestratorServiceAsync(
            ResourceProcedureContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OrchestratorService ??
                throw new InvalidOperationException("The deployment spec should provide the service."));

        public async Task PrepareOrchestratorServiceAsync(
            ResourceOrchestratorServiceProcedureContext context,
            ResourceAction action,
            CancellationToken cancellationToken = default)
        {
            PreparedActions.Add(action);
            PreparedContexts.Add(context);
            PreparedServices.Add(context.Service);
            Operations.Enqueue($"Prepare:{action.Kind}:{context.ReplicaGroup?.Id ?? context.Service.Name}");
            if (gate is not null)
            {
                await gate.SignalAndWaitAsync(cancellationToken);
            }

            if (concurrencyProbe is not null)
            {
                await concurrencyProbe.EnterAndDelayAsync(cancellationToken);
            }
        }

        public Task ReconcileOrchestratorServiceRoutingAsync(
            ResourceOrchestratorServiceProcedureContext context,
            CancellationToken cancellationToken = default)
        {
            RoutedContexts.Add(context);
            Operations.Enqueue($"Route:{context.ReplicaGroup?.Id ?? context.Service.Name}");
            return Task.CompletedTask;
        }

        public Task ExecuteOrchestratorServiceInstanceAsync(
            ResourceOrchestratorServiceInstanceContext context,
            ResourceAction action,
            CancellationToken cancellationToken = default)
        {
            if (failOnStart &&
                action.Kind == ResourceActionKind.Start)
            {
                throw new InvalidOperationException("Replica execution failed.");
            }

            ExecutedActions.Add(action);
            ExecutedInstances.Add(context);
            ExecutedInstanceActions.Enqueue($"{action.Kind}:{context.Instance.Name}");
            Operations.Enqueue($"{action.Kind}:{context.Instance.Name}");
            return Task.CompletedTask;
        }

    }

    private sealed class PassiveResourceOrchestrator(string id) : IResourceOrchestrator
    {
        public string Id => id;

        public string DisplayName => id;

        public bool CanExecute(
            ResourceOrchestrationContext context,
            ResourceAction action) =>
            false;

        public Task<ResourceProcedureResult> ExecuteActionAsync(
            ResourceOrchestrationContext context,
            ResourceAction action,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public bool CanDelete(ResourceOrchestrationContext context) => false;

        public Task<ResourceProcedureResult> DeleteAsync(
            ResourceOrchestrationContext context,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class ConcurrentDeploymentGate(int expectedCount)
    {
        private readonly TaskCompletionSource allArrived =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrived;

        public async Task SignalAndWaitAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref arrived) == expectedCount)
            {
                allArrived.TrySetResult();
            }

            await allArrived.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        }
    }

    private sealed class DeploymentConcurrencyProbe(TimeSpan delay)
    {
        private readonly TaskCompletionSource firstEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int activePreparations;
        private int maxConcurrentPreparations;

        public Task FirstEntered => firstEntered.Task;

        public int MaxConcurrentPreparations => maxConcurrentPreparations;

        public async Task EnterAndDelayAsync(CancellationToken cancellationToken)
        {
            var active = Interlocked.Increment(ref activePreparations);
            UpdateMax(active);
            firstEntered.TrySetResult();
            try
            {
                await Task.Delay(delay, cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref activePreparations);
            }
        }

        private void UpdateMax(int active)
        {
            while (true)
            {
                var current = maxConcurrentPreparations;
                if (active <= current)
                {
                    return;
                }

                if (Interlocked.CompareExchange(
                        ref maxConcurrentPreparations,
                        active,
                        current) == current)
                {
                    return;
                }
            }
        }
    }

    private sealed class TestResourceManagerStore(
        IReadOnlyList<Resource> resources,
        IResourceProvider provider) : IResourceManagerStore
    {
        public IReadOnlyList<IResourceProvider> Providers => [provider];

        public IReadOnlyList<ResourceGroup> GetResourceGroups() => [];

        public IReadOnlyList<Resource> GetAvailableResources() => resources;

        public IReadOnlyList<Resource> GetResources() => resources;

        public IReadOnlyList<ResourceModelDiagnostic> GetResourceModelDiagnostics() => [];

        public ResourceClass? GetResourceTypeClass(string resourceType) => null;

        public Resource? GetResource(string id) =>
            resources.FirstOrDefault(resource =>
                string.Equals(id, resource.Id, StringComparison.OrdinalIgnoreCase));

        public IReadOnlyList<Resource> GetChildren(string resourceId) => [];

        public ResourceGroup? GetGroupForResource(string resourceId) => null;

        public bool IsRegistered(string resourceId) =>
            resources.Any(resource =>
                string.Equals(resourceId, resource.Id, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class TestResourceRegistrationStore(IEnumerable<ResourceRegistration> registrations) :
        IResourceRegistrationStore
    {
        private readonly IReadOnlyList<ResourceRegistration> registrations = registrations.ToArray();

        public IReadOnlyList<ResourceRegistration> GetRegistrations() => registrations;

        public ResourceRegistration? GetRegistration(string resourceId) =>
            registrations.FirstOrDefault(registration =>
                string.Equals(resourceId, registration.ResourceId, StringComparison.OrdinalIgnoreCase));

        public Task RegisterAsync(
            string providerId,
            string resourceId,
            string? resourceGroupId = null,
            IReadOnlyList<string>? dependsOn = null,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RemoveAsync(
            string resourceId,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task AssignToGroupAsync(
            string resourceId,
            string? resourceGroupId,
            IReadOnlyList<string>? dependsOn = null,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SetDependenciesAsync(
            string resourceId,
            IReadOnlyList<string> dependsOn,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "CloudShell.ControlPlane.Tests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class TestOptionsMonitor<TOptions>(TOptions currentValue) :
        IOptionsMonitor<TOptions>
    {
        public TOptions CurrentValue => currentValue;

        public TOptions Get(string? name) => currentValue;

        public IDisposable? OnChange(Action<TOptions, string?> listener) => null;
    }
}
