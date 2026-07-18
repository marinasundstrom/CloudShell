using CloudShell.Abstractions.Usage;
using CloudShell.Hosting.Components.Pages.Usage;

namespace CloudShell.Sample.Tests;

public sealed class UsageSampleAttributeDisplayTests
{
    [Fact]
    public void CreateVisibleAttributes_HidesUsageMetadataAndFormatsOperationalLabels()
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [UsageAttributeNames.DisplayName] = "CPU usage",
            [UsageAttributeNames.Description] = "Container CPU usage.",
            [UsageAttributeNames.Source] = UsageAttributeNames.SourceMonitoring,
            [UsageAttributeNames.MonitoringProvider] = "docker",
            [UsageAttributeNames.MonitoringStatus] = "Available",
            [UsageAttributeNames.MonitoringMessage] = "Metrics collected.",
            ["container.name"] = "api-1",
            ["empty"] = string.Empty
        };

        var visible = UsageSampleAttributeDisplay.CreateVisibleAttributes(attributes);

        Assert.DoesNotContain(visible, attribute =>
            string.Equals(attribute.Name, UsageAttributeNames.DisplayName, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(visible, attribute =>
            string.Equals(attribute.Name, UsageAttributeNames.Description, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(visible, attribute =>
            string.Equals(attribute.Name, UsageAttributeNames.Source, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(visible, attribute =>
            string.Equals(attribute.Name, "empty", StringComparison.OrdinalIgnoreCase));

        Assert.Contains(visible, attribute =>
            attribute.Name == UsageAttributeNames.MonitoringProvider &&
            attribute.Label == "Monitoring provider" &&
            attribute.Value == "docker");
        Assert.Contains(visible, attribute =>
            attribute.Name == UsageAttributeNames.MonitoringStatus &&
            attribute.Label == "Monitoring status" &&
            attribute.Value == "Available");
        Assert.Contains(visible, attribute =>
            attribute.Name == "container.name" &&
            attribute.Label == "container.name" &&
            attribute.Value == "api-1");
    }
}
