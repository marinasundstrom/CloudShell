using CloudShell.Abstractions.Logs;
using Microsoft.EntityFrameworkCore;

namespace CloudShell.Persistence;

public sealed class EfCoreResourceEventStore(
    IDbContextFactory<CloudShellDbContext> contextFactory) : IResourceEventStore
{
    public void Append(ResourceEvent resourceEvent)
    {
        if (string.IsNullOrWhiteSpace(resourceEvent.ResourceId))
        {
            return;
        }

        var enrichedEvent = resourceEvent.WithCurrentTraceContext();
        using var context = contextFactory.CreateDbContext();
        context.ResourceEvents.Add(new ResourceEventEntity
        {
            ResourceId = enrichedEvent.ResourceId.Trim(),
            EventType = Normalize(enrichedEvent.EventType, "event"),
            Message = enrichedEvent.Message.Trim(),
            Timestamp = enrichedEvent.Timestamp,
            TriggeredBy = NormalizeOptional(enrichedEvent.TriggeredBy),
            Level = Normalize(enrichedEvent.Level, "Information"),
            TraceId = NormalizeOptional(enrichedEvent.TraceId),
            SpanId = NormalizeOptional(enrichedEvent.SpanId)
        });
        context.SaveChanges();
    }

    public IReadOnlyList<ResourceEvent> GetEvents(ResourceEventQuery? query = null)
    {
        query ??= new ResourceEventQuery();
        var maxEvents = Math.Clamp(query.MaxEvents, 1, 1000);

        using var context = contextFactory.CreateDbContext();
        IQueryable<ResourceEventEntity> events = context.ResourceEvents.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.ResourceId))
        {
            var resourceId = query.ResourceId.Trim();
            events = events.Where(resourceEvent => resourceEvent.ResourceId == resourceId);
        }

        if (!string.IsNullOrWhiteSpace(query.EventType))
        {
            var eventType = query.EventType.Trim();
            events = events.Where(resourceEvent => resourceEvent.EventType == eventType);
        }

        if (!string.IsNullOrWhiteSpace(query.TriggeredBy))
        {
            var triggeredBy = query.TriggeredBy.Trim();
            events = events.Where(resourceEvent => resourceEvent.TriggeredBy == triggeredBy);
        }

        if (query.Since is not null)
        {
            events = events.Where(resourceEvent => resourceEvent.Timestamp >= query.Since.Value);
        }

        if (query.Before is not null)
        {
            events = events.Where(resourceEvent => resourceEvent.Timestamp < query.Before.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.TraceId))
        {
            var traceId = query.TraceId.Trim();
            events = events.Where(resourceEvent => resourceEvent.TraceId == traceId);
        }

        return events
            .OrderByDescending(resourceEvent => resourceEvent.Timestamp)
            .ThenByDescending(resourceEvent => resourceEvent.Id)
            .Take(maxEvents)
            .Select(resourceEvent => new ResourceEvent(
                resourceEvent.ResourceId,
                resourceEvent.EventType,
                resourceEvent.Message,
                resourceEvent.Timestamp,
                resourceEvent.TriggeredBy,
                resourceEvent.Level,
                resourceEvent.TraceId,
                resourceEvent.SpanId))
            .ToArray();
    }

    private static string Normalize(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
