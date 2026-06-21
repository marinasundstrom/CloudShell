using CloudShell.Abstractions.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace CloudShell.Persistence;

public sealed class EfCoreTelemetryTraceStore(
    IDbContextFactory<CloudShellDbContext> contextFactory,
    IOptions<TelemetryOptions> options) : ITraceStore
{
    private const int MaximumRetainedSpansPerResource = 100_000;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public IReadOnlyList<TraceSpan> GetSpans(
        string? resourceId = null,
        string? traceId = null,
        int maxSpans = 200,
        TelemetryScope? scope = null)
    {
        using var context = contextFactory.CreateDbContext();
        IQueryable<TelemetryTraceSpanEntity> query = context.TelemetryTraceSpans.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(resourceId))
        {
            query = query.Where(span => span.ResourceId == resourceId);
        }

        if (!string.IsNullOrWhiteSpace(traceId))
        {
            query = query.Where(span => span.TraceId == traceId);
        }

        return query
            .OrderByDescending(span => span.StartTime)
            .ThenByDescending(span => span.Id)
            .AsEnumerable()
            .Select(ToSpan)
            .Where(span => scope?.HasAnyFilter != true || scope.Matches(span.SpanAttributes))
            .Take(Math.Clamp(maxSpans, 1, GetRetainedSpansPerResource()))
            .ToArray();
    }

    public void AddSpans(IEnumerable<TraceSpan> spans)
    {
        ArgumentNullException.ThrowIfNull(spans);

        var validSpans = spans
            .Where(span =>
                !string.IsNullOrWhiteSpace(span.TraceId) &&
                !string.IsNullOrWhiteSpace(span.SpanId))
            .ToArray();
        if (validSpans.Length == 0)
        {
            return;
        }

        using var context = contextFactory.CreateDbContext();
        context.TelemetryTraceSpans.AddRange(validSpans.Select(ToEntity));
        context.SaveChanges();

        foreach (var resourceId in validSpans
            .Select(span => span.ResourceId)
            .Where(resourceId => !string.IsNullOrWhiteSpace(resourceId))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            PruneSpans(context, resourceId, GetRetainedSpansPerResource());
        }
    }

    private void PruneSpans(
        CloudShellDbContext context,
        string resourceId,
        int retainedSpans)
    {
        var staleIds = context.TelemetryTraceSpans
            .AsNoTracking()
            .Where(span => span.ResourceId == resourceId)
            .OrderByDescending(span => span.StartTime)
            .ThenByDescending(span => span.Id)
            .Skip(retainedSpans)
            .Select(span => span.Id)
            .ToArray();

        if (staleIds.Length == 0)
        {
            return;
        }

        context.TelemetryTraceSpans
            .Where(span => staleIds.Contains(span.Id))
            .ExecuteDelete();
    }

    private int GetRetainedSpansPerResource() =>
        Math.Clamp(
            options.Value.RetainedSpansPerResource <= 0
                ? 1
                : options.Value.RetainedSpansPerResource,
            1,
            MaximumRetainedSpansPerResource);

    private static TelemetryTraceSpanEntity ToEntity(TraceSpan span) =>
        new()
        {
            TraceId = span.TraceId,
            SpanId = span.SpanId,
            ParentSpanId = span.ParentSpanId,
            Name = span.Name,
            ResourceId = span.ResourceId,
            ServiceName = span.ServiceName,
            Kind = span.Kind,
            Status = span.Status,
            StartTime = span.StartTime,
            DurationTicks = span.Duration.Ticks,
            AttributesJson = JsonSerializer.Serialize(span.SpanAttributes, SerializerOptions)
        };

    private static TraceSpan ToSpan(TelemetryTraceSpanEntity entity) =>
        new(
            entity.TraceId,
            entity.SpanId,
            entity.ParentSpanId,
            entity.Name,
            entity.ResourceId,
            entity.ServiceName,
            entity.Kind,
            entity.Status,
            entity.StartTime,
            TimeSpan.FromTicks(entity.DurationTicks),
            DeserializeAttributes(entity.AttributesJson));

    private static IReadOnlyDictionary<string, string> DeserializeAttributes(string json)
    {
        var attributes = JsonSerializer.Deserialize<IReadOnlyDictionary<string, string>>(
                json,
                SerializerOptions) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return new Dictionary<string, string>(attributes, StringComparer.OrdinalIgnoreCase);
    }
}
