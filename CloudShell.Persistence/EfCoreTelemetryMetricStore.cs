using CloudShell.Abstractions.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace CloudShell.Persistence;

public sealed class EfCoreTelemetryMetricStore(
    IDbContextFactory<CloudShellDbContext> contextFactory,
    IOptions<TelemetryOptions> options) : IMetricStore
{
    private const int MaximumRetainedMetricPointsPerResource = 250_000;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public IReadOnlyList<MetricPoint> GetPoints(
        string? resourceId = null,
        string? metricName = null,
        int maxPoints = 200,
        TelemetryScope? scope = null)
    {
        using var context = contextFactory.CreateDbContext();
        IQueryable<TelemetryMetricPointEntity> query = context.TelemetryMetricPoints.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(resourceId))
        {
            query = query.Where(point => point.ResourceId == resourceId);
        }

        if (!string.IsNullOrWhiteSpace(metricName))
        {
            query = query.Where(point => point.Name == metricName);
        }

        return query
            .OrderByDescending(point => point.Timestamp)
            .ThenByDescending(point => point.Id)
            .AsEnumerable()
            .Select(ToPoint)
            .Where(point => scope?.HasAnyFilter != true || scope.Matches(point.MetricAttributes))
            .Take(Math.Clamp(maxPoints, 1, GetRetainedMetricPointsPerResource()))
            .ToArray();
    }

    public void AddPoints(IEnumerable<MetricPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        var validPoints = points
            .Where(point =>
                !string.IsNullOrWhiteSpace(point.Name) &&
                !string.IsNullOrWhiteSpace(point.ResourceId) &&
                !string.IsNullOrWhiteSpace(point.ServiceName))
            .ToArray();
        if (validPoints.Length == 0)
        {
            return;
        }

        using var context = contextFactory.CreateDbContext();
        context.TelemetryMetricPoints.AddRange(validPoints.Select(ToEntity));
        context.SaveChanges();

        foreach (var resourceId in validPoints
            .Select(point => point.ResourceId)
            .Where(resourceId => !string.IsNullOrWhiteSpace(resourceId))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            PrunePoints(context, resourceId, GetRetainedMetricPointsPerResource());
        }
    }

    private void PrunePoints(
        CloudShellDbContext context,
        string resourceId,
        int retainedPoints)
    {
        var staleIds = context.TelemetryMetricPoints
            .AsNoTracking()
            .Where(point => point.ResourceId == resourceId)
            .OrderByDescending(point => point.Timestamp)
            .ThenByDescending(point => point.Id)
            .Skip(retainedPoints)
            .Select(point => point.Id)
            .ToArray();

        if (staleIds.Length == 0)
        {
            return;
        }

        context.TelemetryMetricPoints
            .Where(point => staleIds.Contains(point.Id))
            .ExecuteDelete();
    }

    private int GetRetainedMetricPointsPerResource() =>
        Math.Clamp(
            options.Value.RetainedMetricPointsPerResource <= 0
                ? 1
                : options.Value.RetainedMetricPointsPerResource,
            1,
            MaximumRetainedMetricPointsPerResource);

    private static TelemetryMetricPointEntity ToEntity(MetricPoint point) =>
        new()
        {
            Name = point.Name,
            ResourceId = point.ResourceId,
            ServiceName = point.ServiceName,
            Value = point.Value,
            Timestamp = point.Timestamp,
            Unit = point.Unit,
            AttributesJson = JsonSerializer.Serialize(point.MetricAttributes, SerializerOptions)
        };

    private static MetricPoint ToPoint(TelemetryMetricPointEntity entity) =>
        new(
            entity.Name,
            entity.ResourceId,
            entity.ServiceName,
            entity.Value,
            entity.Timestamp,
            entity.Unit,
            DeserializeAttributes(entity.AttributesJson));

    private static IReadOnlyDictionary<string, string> DeserializeAttributes(string json)
    {
        var attributes = JsonSerializer.Deserialize<IReadOnlyDictionary<string, string>>(
                json,
                SerializerOptions) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return new Dictionary<string, string>(attributes, StringComparer.OrdinalIgnoreCase);
    }
}
