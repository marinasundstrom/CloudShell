using CloudShell.Abstractions.Logs;
using System.Collections.Concurrent;

namespace CloudShell.ControlPlane.Logs;

public sealed class InMemoryResourceEventStore : IResourceEventStore
{
    private const int MaxEventsPerResource = 1_000;
    private readonly ConcurrentDictionary<string, Queue<StoredResourceEvent>> _events =
        new(StringComparer.OrdinalIgnoreCase);
    private long _sequence;

    public void Append(ResourceEvent resourceEvent)
    {
        if (string.IsNullOrWhiteSpace(resourceEvent.ResourceId))
        {
            return;
        }

        var enrichedEvent = resourceEvent.WithCurrentTraceContext();
        var events = _events.GetOrAdd(enrichedEvent.ResourceId, _ => new Queue<StoredResourceEvent>());
        var storedEvent = new StoredResourceEvent(Interlocked.Increment(ref _sequence), enrichedEvent);
        lock (events)
        {
            events.Enqueue(storedEvent);
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
        IEnumerable<StoredResourceEvent> events = string.IsNullOrWhiteSpace(query.ResourceId)
            ? _events.Values.SelectMany(GetSnapshot)
            : _events.TryGetValue(query.ResourceId, out var resourceEvents)
                ? GetSnapshot(resourceEvents)
                : [];

        if (!string.IsNullOrWhiteSpace(query.EventType))
        {
            events = events.Where(resourceEvent =>
                string.Equals(resourceEvent.Event.EventType, query.EventType, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.TriggeredBy))
        {
            events = events.Where(resourceEvent =>
                string.Equals(resourceEvent.Event.TriggeredBy, query.TriggeredBy, StringComparison.OrdinalIgnoreCase));
        }

        if (query.Since is not null)
        {
            events = events.Where(resourceEvent => resourceEvent.Event.Timestamp >= query.Since.Value);
        }

        if (query.Before is not null)
        {
            events = events.Where(resourceEvent => resourceEvent.Event.Timestamp < query.Before.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.TraceId))
        {
            events = events.Where(resourceEvent =>
                string.Equals(resourceEvent.Event.TraceId, query.TraceId, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.SpanId))
        {
            events = events.Where(resourceEvent =>
                string.Equals(resourceEvent.Event.SpanId, query.SpanId, StringComparison.OrdinalIgnoreCase));
        }

        return events
            .OrderByDescending(resourceEvent => resourceEvent.Event.Timestamp)
            .ThenByDescending(resourceEvent => resourceEvent.Sequence)
            .Take(maxEvents)
            .Select(resourceEvent => resourceEvent.Event)
            .ToArray();
    }

    private static StoredResourceEvent[] GetSnapshot(Queue<StoredResourceEvent> events)
    {
        lock (events)
        {
            return events.ToArray();
        }
    }

    private sealed record StoredResourceEvent(long Sequence, ResourceEvent Event);
}
