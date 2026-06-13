namespace CloudShell.Persistence;

internal sealed class ResourceEventEntity
{
    public long Id { get; set; }

    public string ResourceId { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public DateTimeOffset Timestamp { get; set; }

    public string? TriggeredBy { get; set; }

    public string Level { get; set; } = "Information";

    public string? TraceId { get; set; }

    public string? SpanId { get; set; }
}
