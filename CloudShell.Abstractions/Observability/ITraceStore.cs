namespace CloudShell.Abstractions.Observability;

public interface ITraceStore
{
    IReadOnlyList<TraceSpan> GetSpans(
        string? resourceId = null,
        string? traceId = null,
        int maxSpans = 200,
        TelemetryScope? scope = null);

    void AddSpans(IEnumerable<TraceSpan> spans);
}

public sealed record TraceSpan(
    string TraceId,
    string SpanId,
    string? ParentSpanId,
    string Name,
    string ResourceId,
    string ServiceName,
    string Kind,
    string Status,
    DateTimeOffset StartTime,
    TimeSpan Duration,
    IReadOnlyDictionary<string, string>? Attributes = null)
{
    public IReadOnlyDictionary<string, string> SpanAttributes =>
        Attributes ?? EmptyAttributes;

    private static readonly IReadOnlyDictionary<string, string> EmptyAttributes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
