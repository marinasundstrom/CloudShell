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

            try
            {
                await orchestration.ReconcileReplicaSlotAsync(
                    resource,
                    request.SlotOrdinal,
                    request.Detail,
                    cancellationToken,
                    request.TriggeredBy);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
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
