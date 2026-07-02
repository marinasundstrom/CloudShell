namespace CloudShell.ControlPlane.Usage;

public sealed class UsageRecordingOptions
{
    public const string SectionName = "Usage:Recording";

    public bool Enabled { get; set; } = true;

    public int PollIntervalSeconds { get; set; } = 30;

    public int MaxResourcesPerCycle { get; set; } = 500;

    public string[] IncludedMetricNames { get; set; } = [];

    public string[] ExcludedMetricNames { get; set; } = [];

    public bool ShouldRecordMetric(string metricName)
    {
        if (string.IsNullOrWhiteSpace(metricName))
        {
            return false;
        }

        if (ExcludedMetricNames.Any(name =>
            string.Equals(name, metricName, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return IncludedMetricNames.Length == 0 ||
            IncludedMetricNames.Any(name =>
                string.Equals(name, metricName, StringComparison.OrdinalIgnoreCase));
    }
}
