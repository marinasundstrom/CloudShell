namespace CloudShell.Abstractions.Observability;

public sealed class TelemetryOptions
{
    public const string SectionName = "Observability:Telemetry";

    public string Store { get; set; } = TelemetryStores.InMemory;

    public int RetainedSpansPerResource { get; set; } = 2_000;

    public int RetainedMetricPointsPerResource { get; set; } = 5_000;
}

public static class TelemetryStores
{
    public const string InMemory = "InMemory";

    public const string Database = "Database";
}
