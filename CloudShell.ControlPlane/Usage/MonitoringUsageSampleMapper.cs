using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.Usage;

namespace CloudShell.ControlPlane.Usage;

public sealed class MonitoringUsageSampleMapper
{
    public IReadOnlyList<UsageSample> Map(
        ResourceMonitoringSnapshot snapshot,
        UsageRecordingOptions options)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(options);

        return snapshot.Metrics
            .Where(metric => options.ShouldRecordMetric(metric.Name))
            .Select(metric => Map(snapshot, metric))
            .ToArray();
    }

    private static UsageSample Map(
        ResourceMonitoringSnapshot snapshot,
        ResourceMetricSample metric)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [UsageAttributeNames.Source] = UsageAttributeNames.SourceMonitoring,
            [UsageAttributeNames.MonitoringProvider] = snapshot.Provider
        };

        AddAttribute(attributes, UsageAttributeNames.MonitoringStatus, snapshot.Status);
        AddAttribute(attributes, UsageAttributeNames.MonitoringMessage, snapshot.Message);
        AddAttribute(attributes, UsageAttributeNames.DisplayName, metric.DisplayName);
        AddAttribute(attributes, UsageAttributeNames.Description, metric.Description);
        foreach (var metricAttribute in metric.Attributes ?? new Dictionary<string, string>())
        {
            AddAttribute(attributes, metricAttribute.Key, metricAttribute.Value);
        }

        return new UsageSample(
            metric.Name,
            snapshot.ResourceId,
            metric.Value,
            metric.Timestamp,
            metric.Unit,
            attributes);
    }

    private static void AddAttribute(
        Dictionary<string, string> attributes,
        string name,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(name) &&
            !string.IsNullOrWhiteSpace(value))
        {
            attributes[name] = value;
        }
    }
}
