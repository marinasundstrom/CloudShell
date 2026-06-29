using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceManager.Deployment;
using System.Globalization;

namespace CloudShell.ControlPlane.ResourceManager.Orchestration;

public sealed class ResourceReplicaGroupReconciliationService(
    ResourceOrchestrationService orchestration,
    IResourceManagerStore resourceManager,
    IResourceReplicaGroupReconciliationStore reconciliationStore,
    IResourceOrchestratorDeploymentStore? deploymentStore = null,
    IResourceEventSink? resourceEvents = null)
{
    public Task<IReadOnlyList<ResourceReplicaSlotState>> ListReplicaSlotStatesAsync(
        ResourceReplicaSlotStateQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        query ??= new ResourceReplicaSlotStateQuery();
        var states = reconciliationStore
            .ListRuntimeStates(query.ResourceId)
            .Where(state => query.SlotOrdinal is null || state.SlotOrdinal == query.SlotOrdinal.Value)
            .Where(state => string.IsNullOrWhiteSpace(query.ReplicaGroupId) ||
                string.Equals(state.ReplicaGroupId, query.ReplicaGroupId, StringComparison.OrdinalIgnoreCase))
            .Select(ToReplicaSlotState)
            .Where(state => query.Status is null || state.Status == query.Status.Value)
            .OrderBy(state => state.ResourceId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(state => state.SlotOrdinal)
            .Take(Math.Clamp(query.MaxRecords, 1, 1000))
            .ToArray();

        return Task.FromResult<IReadOnlyList<ResourceReplicaSlotState>>(states);
    }

    public void ObserveUnhealthyReplicaSlot(
        Resource resource,
        int slotOrdinal,
        string? detail = null,
        string? triggeredBy = null)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (slotOrdinal < 1)
        {
            return;
        }

        reconciliationStore.Enqueue(new ResourceReplicaSlotReconciliationRequest(
            resource.Id,
            slotOrdinal,
            detail,
            DateTimeOffset.UtcNow,
            triggeredBy));
    }

    public async Task ProcessPendingAsync(
        int maxCount = 32,
        CancellationToken cancellationToken = default)
    {
        foreach (var request in reconciliationStore.DequeuePending(maxCount))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resource = resourceManager.GetResource(request.ResourceId);
            if (resource is null)
            {
                continue;
            }

            var startedAt = DateTimeOffset.UtcNow;
            var activeGroup = ResolveActiveReplicaGroup(request);
            var currentState = reconciliationStore.GetRuntimeState(request.ResourceId, request.SlotOrdinal) ??
                new ResourceReplicaSlotRuntimeState(
                    request.ResourceId,
                    request.SlotOrdinal,
                    ResourceReplicaSlotRuntimeStatus.Unhealthy,
                    request.Detail,
                    request.ObservedAt,
                    TriggeredBy: request.TriggeredBy);
            reconciliationStore.SetRuntimeState(currentState with
            {
                Status = ResourceReplicaSlotRuntimeStatus.Repairing,
                Detail = request.Detail ?? currentState.Detail,
                ServiceId = activeGroup?.ServiceId ?? currentState.ServiceId,
                ReplicaGroupId = activeGroup?.ReplicaGroup.Id ?? currentState.ReplicaGroupId,
                RuntimeRevisionId = activeGroup?.ReplicaGroup.RuntimeRevisionId ?? currentState.RuntimeRevisionId,
                LastAttemptedAt = startedAt,
                AttemptCount = currentState.AttemptCount + 1,
                TriggeredBy = request.TriggeredBy ?? currentState.TriggeredBy
            });

            try
            {
                var result = await orchestration.ReconcileReplicaSlotAsync(
                    resource,
                    request.SlotOrdinal,
                    request.Detail,
                    cancellationToken,
                    request.TriggeredBy);
                reconciliationStore.SetRuntimeState(currentState with
                {
                    Status = ResourceReplicaSlotRuntimeStatus.Repaired,
                    Detail = request.Detail ?? currentState.Detail,
                    ServiceId = activeGroup?.ServiceId ?? currentState.ServiceId,
                    ReplicaGroupId = activeGroup?.ReplicaGroup.Id ?? currentState.ReplicaGroupId,
                    RuntimeRevisionId = activeGroup?.ReplicaGroup.RuntimeRevisionId ?? currentState.RuntimeRevisionId,
                    LastAttemptedAt = startedAt,
                    LastCompletedAt = DateTimeOffset.UtcNow,
                    AttemptCount = currentState.AttemptCount + 1,
                    TriggeredBy = request.TriggeredBy ?? currentState.TriggeredBy,
                    LastResult = result.Message
                });
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                reconciliationStore.SetRuntimeState(currentState with
                {
                    Status = ResourceReplicaSlotRuntimeStatus.RepairFailed,
                    Detail = request.Detail ?? currentState.Detail,
                    ServiceId = activeGroup?.ServiceId ?? currentState.ServiceId,
                    ReplicaGroupId = activeGroup?.ReplicaGroup.Id ?? currentState.ReplicaGroupId,
                    RuntimeRevisionId = activeGroup?.ReplicaGroup.RuntimeRevisionId ?? currentState.RuntimeRevisionId,
                    LastAttemptedAt = startedAt,
                    LastCompletedAt = DateTimeOffset.UtcNow,
                    AttemptCount = currentState.AttemptCount + 1,
                    TriggeredBy = request.TriggeredBy ?? currentState.TriggeredBy,
                    LastResult = exception.Message
                });
                resourceEvents?.Append(new ResourceEvent(
                    request.ResourceId,
                    ResourceEventTypes.Events.ReplicaManagement.ReconciliationFailed,
                    $"Replica slot {request.SlotOrdinal.ToString(CultureInfo.InvariantCulture)} reconciliation failed: {exception.Message}",
                    DateTimeOffset.UtcNow,
                    request.TriggeredBy,
                ResourceSignalSeverity.Error));
            }
        }
    }

    private static ResourceReplicaSlotState ToReplicaSlotState(
        ResourceReplicaSlotRuntimeState state) =>
        new(
            state.ResourceId,
            state.SlotOrdinal,
            state.ServiceId,
            state.ReplicaGroupId,
            state.RuntimeRevisionId,
            ToPublicStatus(state.Status),
            state.Detail,
            state.ObservedAt,
            state.LastAttemptedAt,
            state.LastCompletedAt,
            state.AttemptCount,
            state.TriggeredBy,
            state.LastResult);

    private static ResourceReplicaSlotReconciliationStatus ToPublicStatus(
        ResourceReplicaSlotRuntimeStatus status) =>
        status switch
        {
            ResourceReplicaSlotRuntimeStatus.Unhealthy => ResourceReplicaSlotReconciliationStatus.Unhealthy,
            ResourceReplicaSlotRuntimeStatus.Repairing => ResourceReplicaSlotReconciliationStatus.Repairing,
            ResourceReplicaSlotRuntimeStatus.Repaired => ResourceReplicaSlotReconciliationStatus.Repaired,
            ResourceReplicaSlotRuntimeStatus.RepairFailed => ResourceReplicaSlotReconciliationStatus.RepairFailed,
            ResourceReplicaSlotRuntimeStatus.Materialized => ResourceReplicaSlotReconciliationStatus.Materialized,
            _ => ResourceReplicaSlotReconciliationStatus.Unhealthy
        };

    private ActiveReplicaGroup? ResolveActiveReplicaGroup(
        ResourceReplicaSlotReconciliationRequest request)
    {
        if (deploymentStore is null)
        {
            return null;
        }

        var record = deploymentStore
            .List(new ResourceOrchestratorDeploymentQuery(
                SourceResourceId: request.ResourceId,
                MaxRecords: 1000))
            .Where(record =>
                record.Status == ResourceOrchestratorDeploymentStatus.Active &&
                record.ReplicaGroup is not null)
            .OrderByDescending(record => record.CompletedAt ?? record.StartedAt)
            .FirstOrDefault();

        return record?.ReplicaGroup is null
            ? null
            : new ActiveReplicaGroup(record.ServiceId, record.ReplicaGroup);
    }

    private sealed record ActiveReplicaGroup(
        string ServiceId,
        ResourceOrchestratorReplicaGroup ReplicaGroup);
}
