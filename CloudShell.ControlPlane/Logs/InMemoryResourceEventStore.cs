using CloudShell.Abstractions.Logs;
using System.Collections.Concurrent;

namespace CloudShell.ControlPlane.Logs;

public sealed class InMemoryResourceEventStore : IResourceEventStore
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

    public IReadOnlyList<ResourceEvent> GetEvents(ResourceEventQuery? query = null)
    {
        query ??= new ResourceEventQuery();
        var maxEvents = Math.Clamp(query.MaxEvents, 1, MaxEventsPerResource);
        IEnumerable<ResourceEvent> events = string.IsNullOrWhiteSpace(query.ResourceId)
            ? _events.Values.SelectMany(GetSnapshot)
            : _events.TryGetValue(query.ResourceId, out var resourceEvents)
                ? GetSnapshot(resourceEvents)
                : [];

        if (!string.IsNullOrWhiteSpace(query.EventType))
        {
            events = events.Where(resourceEvent =>
                string.Equals(resourceEvent.EventType, query.EventType, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.TriggeredBy))
        {
            events = events.Where(resourceEvent =>
                string.Equals(resourceEvent.TriggeredBy, query.TriggeredBy, StringComparison.OrdinalIgnoreCase));
        }

        if (query.Since is not null)
        {
            events = events.Where(resourceEvent => resourceEvent.Timestamp >= query.Since.Value);
        }

        if (query.Before is not null)
        {
            events = events.Where(resourceEvent => resourceEvent.Timestamp < query.Before.Value);
        }

        return events
            .OrderByDescending(resourceEvent => resourceEvent.Timestamp)
            .Take(maxEvents)
            .ToArray();
    }

    private static ResourceEvent[] GetSnapshot(Queue<ResourceEvent> events)
    {
        lock (events)
        {
            return events.ToArray();
        }
    }
}
