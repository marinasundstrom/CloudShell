using CloudShell.Abstractions.Observability;

namespace CloudShell.ControlPlane.Observability;

public sealed class InMemoryTraceStore : ITraceStore
{
    private const int MaxStoredSpans = 2_000;
    private readonly object _gate = new();
    private readonly List<TraceSpan> _spans = [];

    public IReadOnlyList<TraceSpan> GetSpans(
        string? resourceId = null,
        string? traceId = null,
        int maxSpans = 200,
        TelemetryScope? scope = null)
    {
        lock (_gate)
        {
            return _spans
                .Where(span => string.IsNullOrWhiteSpace(resourceId) ||
                    string.Equals(span.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase))
                .Where(span => string.IsNullOrWhiteSpace(traceId) ||
                    string.Equals(span.TraceId, traceId, StringComparison.OrdinalIgnoreCase))
                .Where(span => scope?.HasAnyFilter != true ||
                    scope.Matches(span.SpanAttributes))
                .OrderByDescending(span => span.StartTime)
                .Take(Math.Clamp(maxSpans, 1, MaxStoredSpans))
                .ToArray();
        }
    }

    public void AddSpans(IEnumerable<TraceSpan> spans)
    {
        ArgumentNullException.ThrowIfNull(spans);

        lock (_gate)
        {
            _spans.AddRange(spans.Where(span =>
                !string.IsNullOrWhiteSpace(span.TraceId) &&
                !string.IsNullOrWhiteSpace(span.SpanId)));

            if (_spans.Count > MaxStoredSpans)
            {
                _spans.RemoveRange(0, _spans.Count - MaxStoredSpans);
            }
        }
    }
}
