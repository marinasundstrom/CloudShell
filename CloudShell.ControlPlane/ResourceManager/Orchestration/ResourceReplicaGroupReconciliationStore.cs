using System.Collections.Concurrent;

namespace CloudShell.ControlPlane.ResourceManager.Orchestration;

public interface IResourceReplicaGroupReconciliationStore
{
    void Enqueue(ResourceReplicaSlotReconciliationRequest request);

    IReadOnlyList<ResourceReplicaSlotReconciliationRequest> DequeuePending(int maxCount);

    ResourceReplicaSlotRuntimeState? GetRuntimeState(string resourceId, int slotOrdinal);

    IReadOnlyList<ResourceReplicaSlotRuntimeState> ListRuntimeStates(string? resourceId = null);

    void SetRuntimeState(ResourceReplicaSlotRuntimeState state);

    void DeleteRuntimeState(string resourceId, int slotOrdinal);
}

public sealed record ResourceReplicaSlotReconciliationRequest(
    string ResourceId,
    int SlotOrdinal,
    string? Detail,
    DateTimeOffset ObservedAt,
    string? TriggeredBy);

public sealed record ResourceReplicaSlotRuntimeState(
    string ResourceId,
    int SlotOrdinal,
    ResourceReplicaSlotRuntimeStatus Status,
    string? Detail,
    DateTimeOffset ObservedAt,
    string? ServiceId = null,
    string? ReplicaGroupId = null,
    string? RuntimeRevisionId = null,
    DateTimeOffset? LastAttemptedAt = null,
    DateTimeOffset? LastCompletedAt = null,
    int AttemptCount = 0,
    string? TriggeredBy = null,
    string? LastResult = null);

public enum ResourceReplicaSlotRuntimeStatus
{
    Unhealthy,
    Repairing,
    Repaired,
    RepairFailed,
    Materialized
}

public sealed class InMemoryResourceReplicaGroupReconciliationStore : IResourceReplicaGroupReconciliationStore
{
    private readonly ConcurrentDictionary<string, ResourceReplicaSlotReconciliationRequest> pending =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, ResourceReplicaSlotRuntimeState> runtimeStates =
        new(StringComparer.OrdinalIgnoreCase);

    public void Enqueue(ResourceReplicaSlotReconciliationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var key = CreateKey(request.ResourceId, request.SlotOrdinal);
        pending[key] = request;
        runtimeStates.AddOrUpdate(
            key,
            new ResourceReplicaSlotRuntimeState(
                request.ResourceId,
                request.SlotOrdinal,
                ResourceReplicaSlotRuntimeStatus.Unhealthy,
                request.Detail,
                request.ObservedAt,
                TriggeredBy: request.TriggeredBy),
            (_, current) => current with
            {
                Status = ResourceReplicaSlotRuntimeStatus.Unhealthy,
                Detail = request.Detail,
                ObservedAt = request.ObservedAt,
                TriggeredBy = request.TriggeredBy
            });
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

    public ResourceReplicaSlotRuntimeState? GetRuntimeState(string resourceId, int slotOrdinal)
    {
        if (string.IsNullOrWhiteSpace(resourceId) ||
            slotOrdinal < 1)
        {
            return null;
        }

        return runtimeStates.TryGetValue(CreateKey(resourceId, slotOrdinal), out var state)
            ? state
            : null;
    }

    public IReadOnlyList<ResourceReplicaSlotRuntimeState> ListRuntimeStates(string? resourceId = null)
    {
        IEnumerable<ResourceReplicaSlotRuntimeState> states = runtimeStates.Values;
        if (!string.IsNullOrWhiteSpace(resourceId))
        {
            states = states.Where(state =>
                string.Equals(state.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase));
        }

        return states
            .OrderBy(state => state.ResourceId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(state => state.SlotOrdinal)
            .ToArray();
    }

    public void SetRuntimeState(ResourceReplicaSlotRuntimeState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (string.IsNullOrWhiteSpace(state.ResourceId))
        {
            return;
        }

        if (state.SlotOrdinal < 1)
        {
            return;
        }

        runtimeStates[CreateKey(state.ResourceId, state.SlotOrdinal)] = state;
    }

    public void DeleteRuntimeState(string resourceId, int slotOrdinal)
    {
        if (string.IsNullOrWhiteSpace(resourceId) ||
            slotOrdinal < 1)
        {
            return;
        }

        runtimeStates.TryRemove(CreateKey(resourceId, slotOrdinal), out _);
    }

    private static string CreateKey(string resourceId, int slotOrdinal) =>
        $"{resourceId.Trim()}:{slotOrdinal.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
}
