using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.Usage;
using CloudShell.ControlPlane.Usage;

namespace CloudShell.ControlPlane.Tests;

public sealed class MonitoringUsageSampleMapperTests
{
    [Fact]
    public void Map_ConvertsMonitoringMetricsToUsageSamples()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var snapshot = new ResourceMonitoringSnapshot(
            "resource:test",
            "test-provider",
            timestamp,
            [
                new ResourceMetricSample(
                    "resource.cpu.total",
                    42,
                    "seconds",
                    timestamp,
                    "CPU time",
                    "Total CPU seconds.",
                    new Dictionary<string, string>
                    {
                        ["process.id"] = "123"
                    })
            ],
            "Available",
            "Metrics collected.");

        var samples = new MonitoringUsageSampleMapper().Map(
            snapshot,
            new UsageRecordingOptions());

        var sample = Assert.Single(samples);
        Assert.Equal("resource.cpu.total", sample.Name);
        Assert.Equal("resource:test", sample.ResourceId);
        Assert.Equal(42, sample.Value);
        Assert.Equal("seconds", sample.Unit);
        Assert.Equal(timestamp, sample.Timestamp);
        Assert.Equal(UsageAttributeNames.SourceMonitoring, sample.UsageAttributes[UsageAttributeNames.Source]);
        Assert.Equal("test-provider", sample.UsageAttributes[UsageAttributeNames.MonitoringProvider]);
        Assert.Equal("Available", sample.UsageAttributes[UsageAttributeNames.MonitoringStatus]);
        Assert.Equal("Metrics collected.", sample.UsageAttributes[UsageAttributeNames.MonitoringMessage]);
        Assert.Equal("CPU time", sample.UsageAttributes[UsageAttributeNames.DisplayName]);
        Assert.Equal("Total CPU seconds.", sample.UsageAttributes[UsageAttributeNames.Description]);
        Assert.Equal("123", sample.UsageAttributes["process.id"]);
    }

    [Fact]
    public void Map_AppliesUsageMetricFilters()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var snapshot = new ResourceMonitoringSnapshot(
            "resource:test",
            "test-provider",
            timestamp,
            [
                new ResourceMetricSample("resource.cpu.total", 42, "seconds", timestamp),
                new ResourceMetricSample("resource.memory.usage", 100, "bytes", timestamp),
                new ResourceMetricSample("resource.network.rxBytes", 200, "bytes", timestamp)
            ]);
        var options = new UsageRecordingOptions
        {
            IncludedMetricNames = ["resource.cpu.total", "resource.memory.usage"],
            ExcludedMetricNames = ["resource.memory.usage"]
        };

        var samples = new MonitoringUsageSampleMapper().Map(snapshot, options);

        var sample = Assert.Single(samples);
        Assert.Equal("resource.cpu.total", sample.Name);
    }
}
