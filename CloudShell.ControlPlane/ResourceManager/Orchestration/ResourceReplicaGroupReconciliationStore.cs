using System.Collections.Concurrent;

namespace CloudShell.ControlPlane.ResourceManager.Orchestration;

public interface IResourceReplicaGroupReconciliationStore
{
    void Enqueue(ResourceReplicaSlotReconciliationRequest request);

    IReadOnlyList<ResourceReplicaSlotReconciliationRequest> DequeuePending(int maxCount);
}

public sealed record ResourceReplicaSlotReconciliationRequest(
    string ResourceId,
    int SlotOrdinal,
    string? Detail,
    DateTimeOffset ObservedAt,
    string? TriggeredBy);

public sealed class InMemoryResourceReplicaGroupReconciliationStore : IResourceReplicaGroupReconciliationStore
{
    private readonly ConcurrentDictionary<string, ResourceReplicaSlotReconciliationRequest> pending =
        new(StringComparer.OrdinalIgnoreCase);

    public void Enqueue(ResourceReplicaSlotReconciliationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        pending[CreateKey(request.ResourceId, request.SlotOrdinal)] = request;
    }

    public IReadOnlyList<ResourceReplicaSlotReconciliationRequest> DequeuePending(int maxCount)
    {
        if (maxCount < 1)
        {
            return [];
        }

        var requests = new List<ResourceReplicaSlotReconciliationRequest>(maxCount);
        foreach (var key in pending.Keys.Take(maxCount))
        {
            if (pending.TryRemove(key, out var request))
            {
                requests.Add(request);
            }
        }

        return requests;
    }

    private static string CreateKey(string resourceId, int slotOrdinal) =>
        $"{resourceId.Trim()}:{slotOrdinal.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
}
