namespace CloudShell.Persistence;

internal sealed class UsageSampleEntity
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string ResourceId { get; set; } = string.Empty;

    public double Value { get; set; }

    public DateTimeOffset Timestamp { get; set; }

    public string? Unit { get; set; }

    public string AttributesJson { get; set; } = "{}";
}
