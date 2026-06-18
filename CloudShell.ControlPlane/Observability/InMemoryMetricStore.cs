using CloudShell.Abstractions.Observability;

namespace CloudShell.ControlPlane.Observability;

public sealed class InMemoryMetricStore : IMetricStore
{
    private const int MaxStoredPoints = 5_000;
    private readonly object _gate = new();
    private readonly List<MetricPoint> _points = [];

    public IReadOnlyList<MetricPoint> GetPoints(
        string? resourceId = null,
        string? metricName = null,
        int maxPoints = 200,
        TelemetryScope? scope = null)
    {
        lock (_gate)
        {
            return _points
                .Where(point => string.IsNullOrWhiteSpace(resourceId) ||
                    string.Equals(point.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase))
                .Where(point => string.IsNullOrWhiteSpace(metricName) ||
                    string.Equals(point.Name, metricName, StringComparison.OrdinalIgnoreCase))
                .Where(point => scope?.HasAnyFilter != true ||
                    scope.Matches(point.MetricAttributes))
                .OrderByDescending(point => point.Timestamp)
                .Take(Math.Clamp(maxPoints, 1, MaxStoredPoints))
                .ToArray();
        }
    }

    public void AddPoints(IEnumerable<MetricPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        lock (_gate)
        {
            _points.AddRange(points.Where(point =>
                !string.IsNullOrWhiteSpace(point.Name) &&
                !string.IsNullOrWhiteSpace(point.ResourceId) &&
                !string.IsNullOrWhiteSpace(point.ServiceName)));

            if (_points.Count > MaxStoredPoints)
            {
                _points.RemoveRange(0, _points.Count - MaxStoredPoints);
            }
        }
    }
}
