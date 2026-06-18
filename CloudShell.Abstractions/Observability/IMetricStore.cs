using System.Text.Json.Serialization;

namespace CloudShell.Abstractions.Observability;

public interface IMetricStore
{
    IReadOnlyList<MetricPoint> GetPoints(
        string? resourceId = null,
        string? metricName = null,
        int maxPoints = 200,
        TelemetryScope? scope = null);

    void AddPoints(IEnumerable<MetricPoint> points);
}

public sealed record MetricPoint(
    string Name,
    string ResourceId,
    string ServiceName,
    double Value,
    DateTimeOffset Timestamp,
    string? Unit = null,
    IReadOnlyDictionary<string, string>? Attributes = null)
{
    [JsonIgnore]
    public IReadOnlyDictionary<string, string> MetricAttributes =>
        Attributes ?? EmptyAttributes;

    private static readonly IReadOnlyDictionary<string, string> EmptyAttributes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed record MetricQuery(
    string? ResourceId = null,
    string? MetricName = null,
    int MaxPoints = 200,
    TelemetryScope? Scope = null);
