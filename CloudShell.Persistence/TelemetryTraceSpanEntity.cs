namespace CloudShell.Persistence;

internal sealed class TelemetryTraceSpanEntity
{
    public long Id { get; set; }

    public string TraceId { get; set; } = string.Empty;

    public string SpanId { get; set; } = string.Empty;

    public string? ParentSpanId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string ResourceId { get; set; } = string.Empty;

    public string ServiceName { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset StartTime { get; set; }

    public long DurationTicks { get; set; }

    public string AttributesJson { get; set; } = "{}";
}
