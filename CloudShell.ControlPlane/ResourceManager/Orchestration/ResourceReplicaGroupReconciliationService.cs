using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using System.Globalization;

namespace CloudShell.ControlPlane.ResourceManager.Orchestration;

public sealed class ResourceReplicaGroupReconciliationService(
    ResourceOrchestrationService orchestration,
    IResourceManagerStore resourceManager,
    IResourceReplicaGroupReconciliationStore reconciliationStore,
    IResourceEventSink? resourceEvents = null)
{
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
}
