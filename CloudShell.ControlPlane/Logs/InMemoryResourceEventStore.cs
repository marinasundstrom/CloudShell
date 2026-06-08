using CloudShell.Abstractions.Logs;
using System.Collections.Concurrent;

namespace CloudShell.ControlPlane.Logs;

public sealed class InMemoryResourceEventStore : IResourceEventSink
{
    private const int MaxEventsPerResource = 1_000;
    private readonly ConcurrentDictionary<string, Queue<ResourceEvent>> _events =
        new(StringComparer.OrdinalIgnoreCase);

    public void Append(ResourceEvent resourceEvent)
    {
        if (string.IsNullOrWhiteSpace(resourceEvent.ResourceId))
        {
            return;
        }

        var events = _events.GetOrAdd(resourceEvent.ResourceId, _ => new Queue<ResourceEvent>());
        lock (events)
        {
            events.Enqueue(resourceEvent);
            while (events.Count > MaxEventsPerResource)
            {
                events.Dequeue();
            }
        }
    }

    public IReadOnlyList<ResourceEvent> GetEvents(
        string resourceId,
        int maxEntries,
        DateTimeOffset? before)
    {
        if (!_events.TryGetValue(resourceId, out var events))
        {
            return [];
        }

        lock (events)
        {
            var query = events.AsEnumerable();
            if (before is not null)
            {
                query = query.Where(resourceEvent => resourceEvent.Timestamp < before.Value);
            }

            return query
                .TakeLast(Math.Max(1, maxEntries))
                .ToArray();
        }
    }
}
