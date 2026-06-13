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

        using var context = contextFactory.CreateDbContext();
        context.ResourceEvents.Add(new ResourceEventEntity
        {
            ResourceId = resourceEvent.ResourceId.Trim(),
            EventType = Normalize(resourceEvent.EventType, "event"),
            Message = resourceEvent.Message.Trim(),
            Timestamp = resourceEvent.Timestamp,
            TriggeredBy = NormalizeOptional(resourceEvent.TriggeredBy),
            Level = Normalize(resourceEvent.Level, "Information")
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
                resourceEvent.Level))
            .ToArray();
    }

    private static string Normalize(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
