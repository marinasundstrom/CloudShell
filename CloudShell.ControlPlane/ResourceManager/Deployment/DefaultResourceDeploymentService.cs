using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceManager.Orchestration;
using System.Globalization;

namespace CloudShell.ControlPlane.ResourceManager.Deployment;

public sealed class DefaultResourceDeploymentService(
    IResourceOrchestratorDeploymentStore? deploymentStore = null,
    IResourceReplicaGroupReconciliationStore? replicaGroupReconciliationStore = null) :
    IResourceOrchestratorDeploymentApplier
{
    public bool CanApplyDeployment(
        ResourceOrchestrationContext context,
        ResourceOrchestratorDeployment deployment) =>
        ResourceOrchestratorProviderResolver.GetServiceProcedureProvider(context, ResourceAction.Start) is not null;

    public async Task<ResourceOrchestratorDeploymentApplyResult> ApplyDeploymentAsync(
        ResourceOrchestrationContext context,
        ResourceOrchestratorDeployment deployment,
        CancellationToken cancellationToken = default)
    {
        var deploymentDefinition = deployment.Spec.CreateDeploymentDefinition(deployment.RevisionId);
        deployment = deployment with
        {
            Spec = deployment.Spec with
            {
                Definition = deploymentDefinition
            }
        };
        var provider = ResourceOrchestratorProviderResolver.GetServiceProcedureProvider(context, ResourceAction.Start)
            ?? throw new ControlPlaneException(
                ControlPlaneError.ResourceActionUnsupported(context.Resource.Name));
        var resourceContext = ResourceOrchestratorProviderResolver.CreateProcedureContext(context);
        var service = deployment.Spec.Service with
        {
            RuntimeRevisionId = deployment.RevisionId
        };
        var targetReplicaGroup = ResolveTargetReplicaGroup(deploymentDefinition, service);
        var replicaGroup = targetReplicaGroup.ReplicaGroup;
        var routingBindings = targetReplicaGroup.RoutingBindings;
        var previousReplicaGroup = GetLatestActiveReplicaGroup(deployment);
        try
        {
            if (context.Resource.State is ResourceState.Running or ResourceState.Degraded &&
                previousReplicaGroup is not null &&
                string.Equals(previousReplicaGroup.Id, replicaGroup.Id, StringComparison.OrdinalIgnoreCase))
            {
                await ApplyReplicaGroupChangeAsync(
                    provider,
                    resourceContext,
                    service,
                    previousReplicaGroup,
                    replicaGroup,
                    routingBindings,
                    targetReplicaGroup.ReconciliationPolicy,
                    deployment,
                    cancellationToken);
            }
            else if (context.Resource.State is ResourceState.Running or ResourceState.Degraded &&
                previousReplicaGroup is not null)
            {
                await ApplyReplicaGroupReplacementAsync(
                    provider,
                    resourceContext,
                    service,
                    previousReplicaGroup,
                    replicaGroup,
                    routingBindings,
                    targetReplicaGroup.ReconciliationPolicy,
                    deployment,
                    cancellationToken);
            }
            else
            {
                await ResourceOrchestratorServiceExecutor.ExecuteServiceActionAsync(
                    provider,
                    resourceContext,
                    service,
                    ResourceAction.Start,
                    cancellationToken,
                    deployment,
                    replicaGroup,
                    routingBindings);
            }

            EnsureRequestedReplicaSlotsMaterialized(replicaGroup, deployment);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RollBackFailedDeploymentAsync(
                provider,
                resourceContext,
                service,
                replicaGroup,
                deployment,
                exception,
                cancellationToken);
            throw;
        }

        var applied = deployment with { Status = ResourceOrchestratorDeploymentStatus.Active };
        var retiredReplicaGroups = CreateRetiredReplicaGroups(
            service,
            previousReplicaGroup,
            replicaGroup,
            targetReplicaGroup.ReconciliationPolicy,
            applied);
        var revisionCreatedAt = DateTimeOffset.UtcNow;
        var revision = deploymentStore?.CreateRevision(
            applied,
            revisionCreatedAt,
            ResourceOrchestratorRevisionStatus.Active,
            replicaGroup,
            context.TriggeredBy) ??
            new ResourceOrchestratorRevision(
                CreateFallbackEnvironmentRevisionId(applied, revisionCreatedAt),
                applied.Id,
                applied.SourceResourceId,
                applied.ServiceId,
                RevisionNumber: 1,
                revisionCreatedAt,
                ResourceOrchestratorRevisionStatus.Active,
                replicaGroup,
                applied.BasedOnRevisionId,
                context.TriggeredBy,
                applied.Spec.CreateDeploymentDefinition(applied.RevisionId));
        RecordMaterializedReplicaSlots(
            context.Resource.Id,
            service,
            replicaGroup,
            revisionCreatedAt,
            context.TriggeredBy);
        return new ResourceOrchestratorDeploymentApplyResult(
            applied,
            revision,
            ResourceProcedureResult.Completed(
                $"Applied deployment '{deployment.Id}' for runtime revision '{deployment.RevisionId}'."),
            retiredReplicaGroups,
            previousReplicaGroup);
    }

    private void RecordMaterializedReplicaSlots(
        string resourceId,
        ResourceOrchestratorService service,
        ResourceOrchestratorReplicaGroup replicaGroup,
        DateTimeOffset observedAt,
        string? triggeredBy)
    {
        if (replicaGroupReconciliationStore is null)
        {
            return;
        }

        var activeSlotOrdinals = replicaGroup
            .Slots
            .Select(slot => slot.Ordinal)
            .ToHashSet();
        foreach (var state in replicaGroupReconciliationStore.ListRuntimeStates(resourceId))
        {
            if (!activeSlotOrdinals.Contains(state.SlotOrdinal))
            {
                replicaGroupReconciliationStore.DeleteRuntimeState(resourceId, state.SlotOrdinal);
            }
        }

        foreach (var slot in replicaGroup.Slots)
        {
            replicaGroupReconciliationStore.SetRuntimeState(new ResourceReplicaSlotRuntimeState(
                resourceId,
                slot.Ordinal,
                ResourceReplicaSlotRuntimeStatus.Materialized,
                slot.IsOccupied
                    ? $"Replica slot {slot.Ordinal.ToString(CultureInfo.InvariantCulture)} is materialized."
                    : $"Replica slot {slot.Ordinal.ToString(CultureInfo.InvariantCulture)} is requested but has no occupant.",
                observedAt,
                service.Name,
                replicaGroup.Id,
                replicaGroup.RuntimeRevisionId,
                LastCompletedAt: observedAt,
                TriggeredBy: triggeredBy,
                LastResult: $"Deployment materialized replica slot {slot.Ordinal.ToString(CultureInfo.InvariantCulture)}."));
        }
    }

    private ResourceOrchestratorReplicaGroup? GetLatestActiveReplicaGroup(
        ResourceOrchestratorDeployment deployment)
    {
        if (deploymentStore is null)
        {
            return null;
        }

        return deploymentStore
            .List(new ResourceOrchestratorDeploymentQuery(
                SourceResourceId: deployment.SourceResourceId,
                OrchestratorId: deployment.OrchestratorId,
                MaxRecords: 1_000))
            .Where(record =>
                record.Status == ResourceOrchestratorDeploymentStatus.Active &&
                string.Equals(record.ServiceId, deployment.ServiceId, StringComparison.OrdinalIgnoreCase) &&
                record.ReplicaGroup is not null)
            .OrderByDescending(record => record.CompletedAt ?? record.StartedAt)
            .Select(record => record.ReplicaGroup)
            .FirstOrDefault();
    }

    private static TargetReplicaGroup ResolveTargetReplicaGroup(
        ResourceOrchestratorDeploymentDefinition definition,
        ResourceOrchestratorService service)
    {
        var serviceDefinition = definition
            .DeploymentServices
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Name, service.Name, StringComparison.OrdinalIgnoreCase));
        var replicaGroupDefinition = serviceDefinition
            ?.ReplicaGroupDefinitions
            .FirstOrDefault();

        var replicaGroup = replicaGroupDefinition is null
            ? ResourceOrchestratorReplicaGroups.CreateDefaultReplicaGroup(service)
            : replicaGroupDefinition.ToReplicaGroup(service);
        var routingBindings = ResolveRoutingBindings(serviceDefinition, replicaGroup);

        return replicaGroupDefinition is null
            ? new TargetReplicaGroup(
                replicaGroup,
                ResourceOrchestratorReplicaGroupReconciliationPolicy.Default,
                routingBindings)
            : new TargetReplicaGroup(
                replicaGroup,
                replicaGroupDefinition.ReconciliationPolicy ??
                    ResourceOrchestratorReplicaGroupReconciliationPolicy.Default,
                routingBindings);
    }

    private static IReadOnlyList<ResourceOrchestratorServiceRoutingBindingDefinition> ResolveRoutingBindings(
        ResourceOrchestratorServiceDefinition? serviceDefinition,
        ResourceOrchestratorReplicaGroup replicaGroup)
    {
        if (serviceDefinition is null)
        {
            return [];
        }

        return serviceDefinition
            .RoutingBindingDefinitions
            .Where(binding =>
                string.Equals(binding.ReplicaGroupName, replicaGroup.Id, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static IReadOnlyList<ResourceOrchestratorReplicaGroupTearDownRequest> CreateRetiredReplicaGroups(
        ResourceOrchestratorService targetService,
        ResourceOrchestratorReplicaGroup? previousReplicaGroup,
        ResourceOrchestratorReplicaGroup targetReplicaGroup,
        ResourceOrchestratorReplicaGroupReconciliationPolicy reconciliationPolicy,
        ResourceOrchestratorDeployment deployment)
    {
        if (previousReplicaGroup is null ||
            string.Equals(previousReplicaGroup.Id, targetReplicaGroup.Id, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var retainedSlots = Math.Clamp(
            reconciliationPolicy.RetainPreviousReplicaSlots,
            0,
            previousReplicaGroup.RequestedReplicaSlots);
        if (retainedSlots == previousReplicaGroup.RequestedReplicaSlots)
        {
            return [];
        }

        var replicaGroupToTearDown = previousReplicaGroup;
        if (retainedSlots > 0)
        {
            var tearDownSlots = Math.Max(1, previousReplicaGroup.RequestedReplicaSlots - retainedSlots);
            var instancesToTearDown = previousReplicaGroup
                .Instances
                .OrderByDescending(instance => instance.ReplicaOrdinal)
                .Take(tearDownSlots)
                .OrderBy(instance => instance.ReplicaOrdinal)
                .ToArray();
            replicaGroupToTearDown = previousReplicaGroup with
            {
                RequestedReplicaSlots = instancesToTearDown.Length,
                Instances = instancesToTearDown
            };
        }

        var previousService = targetService with
        {
            RuntimeRevisionId = previousReplicaGroup.RuntimeRevisionId,
            Workload = targetService.Workload with
            {
                Replicas = Math.Max(1, replicaGroupToTearDown.RequestedReplicas),
                ReplicasEnabled = Math.Max(1, replicaGroupToTearDown.RequestedReplicas) > 1
            }
        };
        return
        [
            new ResourceOrchestratorReplicaGroupTearDownRequest(
                previousService,
                replicaGroupToTearDown,
                retainedSlots == 0
                    ? $"Deployment '{deployment.Id}' replaced runtime replica group '{previousReplicaGroup.Id}' with '{targetReplicaGroup.Id}'."
                    : $"Deployment '{deployment.Id}' replaced runtime replica group '{previousReplicaGroup.Id}' with '{targetReplicaGroup.Id}' and retained {retainedSlots.ToString(CultureInfo.InvariantCulture)} previous replica slot{Pluralize(retainedSlots)}.")
        ];
    }

    private static async Task ApplyReplicaGroupChangeAsync(
        IResourceOrchestratorServiceProcedureProvider provider,
        ResourceProcedureContext resourceContext,
        ResourceOrchestratorService service,
        ResourceOrchestratorReplicaGroup previousReplicaGroup,
        ResourceOrchestratorReplicaGroup targetReplicaGroup,
        IReadOnlyList<ResourceOrchestratorServiceRoutingBindingDefinition> routingBindings,
        ResourceOrchestratorReplicaGroupReconciliationPolicy reconciliationPolicy,
        ResourceOrchestratorDeployment deployment,
        CancellationToken cancellationToken)
    {
        var change = ResourceOrchestratorReplicaGroups.CreateChange(previousReplicaGroup, targetReplicaGroup);
        ResourceOrchestratorServiceExecutor.AppendDeploymentEvent(
            resourceContext,
            ResourceEventTypes.Events.Deployment.ServiceReconciling,
            $"Reconciling orchestrator service '{deployment.ServiceId}' replica group '{targetReplicaGroup.Id}' from {previousReplicaGroup.RequestedReplicaSlots.ToString(CultureInfo.InvariantCulture)} to {targetReplicaGroup.RequestedReplicaSlots.ToString(CultureInfo.InvariantCulture)} requested replica slots for deployment '{deployment.Id}'.");

        if (change.AddedInstances.Count > 0)
        {
            if (reconciliationPolicy.ScaleOutRoutingMode ==
                ResourceOrchestratorScaleOutRoutingMode.BeforeAddedReplicas)
            {
                await ReconcileRoutingAsync(
                    provider,
                    resourceContext,
                    service,
                    targetReplicaGroup,
                    routingBindings,
                    deployment,
                    cancellationToken);
            }

            await provider.PrepareOrchestratorServiceAsync(
                new ResourceOrchestratorServiceProcedureContext(
                    resourceContext,
                    service,
                    targetReplicaGroup,
                    routingBindings),
                ResourceAction.Start,
                cancellationToken);

            foreach (var instance in change.AddedInstances)
            {
                ResourceOrchestratorServiceExecutor.AppendDeploymentEvent(
                    resourceContext,
                    ResourceEventTypes.Events.Deployment.ReplicaMaterializing,
                    $"Materializing replica {FormatReplicaPosition(instance)} '{instance.Name}' for deployment '{deployment.Id}'.");
                await provider.ExecuteOrchestratorServiceInstanceAsync(
                    new ResourceOrchestratorServiceInstanceContext(resourceContext, service, instance, targetReplicaGroup),
                    ResourceAction.Start,
                    cancellationToken);
                ResourceOrchestratorServiceExecutor.AppendDeploymentEvent(
                    resourceContext,
                    ResourceEventTypes.Events.Deployment.ReplicaMaterialized,
                    $"Materialized replica {FormatReplicaPosition(instance)} '{instance.Name}' for deployment '{deployment.Id}'.");
            }

            if (reconciliationPolicy.ScaleOutRoutingMode ==
                ResourceOrchestratorScaleOutRoutingMode.AfterAddedReplicas)
            {
                await ReconcileRoutingAsync(
                    provider,
                    resourceContext,
                    service,
                    targetReplicaGroup,
                    routingBindings,
                    deployment,
                    cancellationToken);
            }
        }

        if (change.RemovedInstances.Count > 0 &&
            reconciliationPolicy.ScaleInRoutingMode ==
                ResourceOrchestratorScaleInRoutingMode.BeforeRemovedReplicas)
        {
            await ReconcileRoutingAsync(
                provider,
                resourceContext,
                service,
                targetReplicaGroup,
                routingBindings,
                deployment,
                cancellationToken);
        }

        foreach (var instance in change.RemovedInstances.OrderByDescending(instance => instance.ReplicaOrdinal))
        {
            await provider.ExecuteOrchestratorServiceInstanceAsync(
                new ResourceOrchestratorServiceInstanceContext(resourceContext, service, instance, previousReplicaGroup),
                ResourceAction.Stop,
                cancellationToken);
        }

        if (change.RemovedInstances.Count > 0 &&
            reconciliationPolicy.ScaleInRoutingMode ==
                ResourceOrchestratorScaleInRoutingMode.AfterRemovedReplicas)
        {
            await ReconcileRoutingAsync(
                provider,
                resourceContext,
                service,
                targetReplicaGroup,
                routingBindings,
                deployment,
                cancellationToken);
        }

        ResourceOrchestratorServiceExecutor.AppendDeploymentEvent(
            resourceContext,
            ResourceEventTypes.Events.Deployment.ServiceReconciled,
            $"Reconciled orchestrator service '{deployment.ServiceId}' replica group '{targetReplicaGroup.Id}' to {targetReplicaGroup.OccupiedReplicaSlots.ToString(CultureInfo.InvariantCulture)}/{targetReplicaGroup.RequestedReplicaSlots.ToString(CultureInfo.InvariantCulture)} occupied replica slots for deployment '{deployment.Id}'.");
    }

    private static async Task ApplyReplicaGroupReplacementAsync(
        IResourceOrchestratorServiceProcedureProvider provider,
        ResourceProcedureContext resourceContext,
        ResourceOrchestratorService service,
        ResourceOrchestratorReplicaGroup previousReplicaGroup,
        ResourceOrchestratorReplicaGroup targetReplicaGroup,
        IReadOnlyList<ResourceOrchestratorServiceRoutingBindingDefinition> routingBindings,
        ResourceOrchestratorReplicaGroupReconciliationPolicy reconciliationPolicy,
        ResourceOrchestratorDeployment deployment,
        CancellationToken cancellationToken)
    {
        ResourceOrchestratorServiceExecutor.AppendDeploymentEvent(
            resourceContext,
            ResourceEventTypes.Events.Deployment.ServiceReconciling,
            $"Replacing orchestrator service '{deployment.ServiceId}' replica group '{previousReplicaGroup.Id}' with '{targetReplicaGroup.Id}' for deployment '{deployment.Id}'.");

        if (reconciliationPolicy.ReplacementRoutingMode ==
            ResourceOrchestratorReplacementRoutingMode.BeforeNewReplicaGroupMaterialized)
        {
            await ReconcileRoutingAsync(
                provider,
                resourceContext,
                service,
                targetReplicaGroup,
                routingBindings,
                deployment,
                cancellationToken);
        }

        await provider.PrepareOrchestratorServiceAsync(
            new ResourceOrchestratorServiceProcedureContext(
                resourceContext,
                service,
                targetReplicaGroup,
                routingBindings),
            ResourceAction.Start,
            cancellationToken);

        foreach (var instance in targetReplicaGroup.Instances)
        {
            ResourceOrchestratorServiceExecutor.AppendDeploymentEvent(
                resourceContext,
                ResourceEventTypes.Events.Deployment.ReplicaMaterializing,
                $"Materializing replacement replica {FormatReplicaPosition(instance)} '{instance.Name}' for deployment '{deployment.Id}'.");
            await provider.ExecuteOrchestratorServiceInstanceAsync(
                new ResourceOrchestratorServiceInstanceContext(resourceContext, service, instance, targetReplicaGroup),
                ResourceAction.Start,
                cancellationToken);
            ResourceOrchestratorServiceExecutor.AppendDeploymentEvent(
                resourceContext,
                ResourceEventTypes.Events.Deployment.ReplicaMaterialized,
                $"Materialized replacement replica {FormatReplicaPosition(instance)} '{instance.Name}' for deployment '{deployment.Id}'.");
        }

        if (reconciliationPolicy.ReplacementRoutingMode ==
            ResourceOrchestratorReplacementRoutingMode.AfterNewReplicaGroupMaterialized)
        {
            await ReconcileRoutingAsync(
                provider,
                resourceContext,
                service,
                targetReplicaGroup,
                routingBindings,
                deployment,
                cancellationToken);
        }

        ResourceOrchestratorServiceExecutor.AppendDeploymentEvent(
            resourceContext,
            ResourceEventTypes.Events.Deployment.ServiceReconciled,
            $"Replaced orchestrator service '{deployment.ServiceId}' replica group '{previousReplicaGroup.Id}' with '{targetReplicaGroup.Id}' for deployment '{deployment.Id}'.");
    }

    private static async Task ReconcileRoutingAsync(
        IResourceOrchestratorServiceProcedureProvider provider,
        ResourceProcedureContext resourceContext,
        ResourceOrchestratorService service,
        ResourceOrchestratorReplicaGroup targetReplicaGroup,
        IReadOnlyList<ResourceOrchestratorServiceRoutingBindingDefinition> routingBindings,
        ResourceOrchestratorDeployment deployment,
        CancellationToken cancellationToken)
    {
        if (service.ServicePorts.Count == 0)
        {
            return;
        }

        ResourceOrchestratorServiceExecutor.AppendDeploymentEvent(
            resourceContext,
            ResourceEventTypes.Events.Deployment.RoutingUpdating,
            $"Updating routing for orchestrator service '{deployment.ServiceId}' to replica group '{targetReplicaGroup.Id}' for deployment '{deployment.Id}'.");
        await provider.ReconcileOrchestratorServiceRoutingAsync(
            new ResourceOrchestratorServiceProcedureContext(
                resourceContext,
                service,
                targetReplicaGroup,
                routingBindings),
            cancellationToken);
        ResourceOrchestratorServiceExecutor.AppendDeploymentEvent(
            resourceContext,
            ResourceEventTypes.Events.Deployment.RoutingUpdated,
            $"Updated routing for orchestrator service '{deployment.ServiceId}' to replica group '{targetReplicaGroup.Id}' for deployment '{deployment.Id}'.");
    }

    private static void EnsureRequestedReplicaSlotsMaterialized(
        ResourceOrchestratorReplicaGroup replicaGroup,
        ResourceOrchestratorDeployment deployment)
    {
        if (replicaGroup.HasRequestedSlotsMaterialized)
        {
            return;
        }

        throw new ControlPlaneException(new ControlPlaneError(
            ControlPlaneErrorCodes.OperationFailed,
            $"Deployment '{deployment.Id}' requested {replicaGroup.RequestedReplicaSlots.ToString(CultureInfo.InvariantCulture)} replica slots but materialized {replicaGroup.OccupiedReplicaSlots.ToString(CultureInfo.InvariantCulture)} occupied slots."));
    }

    private static string FormatReplicaPosition(ResourceOrchestratorServiceInstance instance) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{instance.ReplicaOrdinal}/{instance.ReplicaCount}");

    private static string Pluralize(int count) =>
        count == 1 ? string.Empty : "s";

    private static async Task RollBackFailedDeploymentAsync(
        IResourceOrchestratorServiceProcedureProvider provider,
        ResourceProcedureContext resourceContext,
        ResourceOrchestratorService service,
        ResourceOrchestratorReplicaGroup replicaGroup,
        ResourceOrchestratorDeployment deployment,
        Exception applyException,
        CancellationToken cancellationToken)
    {
        ResourceOrchestratorServiceExecutor.AppendDeploymentEvent(
            resourceContext,
            ResourceEventTypes.Events.Deployment.RollingBack,
            $"Rolling back deployment '{deployment.Id}' for runtime revision '{deployment.RevisionId}' after apply failed. Reason: {applyException.Message}",
            ResourceSignalSeverity.Warning);

        try
        {
            await ResourceOrchestratorServiceExecutor.ExecuteReplicaGroupAsync(
                provider,
                resourceContext,
                service,
                replicaGroup,
                ResourceAction.Stop,
                deployment: null,
                cancellationToken);
        }
        catch (Exception rollbackException) when (rollbackException is not OperationCanceledException)
        {
            ResourceOrchestratorServiceExecutor.AppendDeploymentEvent(
                resourceContext,
                ResourceEventTypes.Events.Deployment.RollbackFailed,
                $"Rollback failed for deployment '{deployment.Id}' runtime revision '{deployment.RevisionId}'. Reason: {rollbackException.Message}",
                ResourceSignalSeverity.Error);
            return;
        }

        ResourceOrchestratorServiceExecutor.AppendDeploymentEvent(
            resourceContext,
            ResourceEventTypes.Events.Deployment.RolledBack,
            $"Rolled back deployment '{deployment.Id}' for runtime revision '{deployment.RevisionId}' by tearing down replica group '{replicaGroup.Id}'.",
            ResourceSignalSeverity.Warning);
    }

    private static ResourceOrchestratorEnvironmentRevisionId CreateFallbackEnvironmentRevisionId(
        ResourceOrchestratorDeployment deployment,
        DateTimeOffset createdAt) =>
        new(
            string.Create(
                CultureInfo.InvariantCulture,
                $"env-{deployment.Id}-{createdAt:yyyyMMddHHmmssfff}"));

    private sealed record TargetReplicaGroup(
        ResourceOrchestratorReplicaGroup ReplicaGroup,
        ResourceOrchestratorReplicaGroupReconciliationPolicy ReconciliationPolicy,
        IReadOnlyList<ResourceOrchestratorServiceRoutingBindingDefinition> RoutingBindings);
}
