using CloudShell.ControlPlane.Providers;
using CloudShell.ResourceHost;
using Microsoft.Extensions.Configuration;

namespace CloudShell.Sample.Tests;

public sealed class DesignPreviewWidgetTests
{
    [Fact]
    public async Task PreviewUsesRealUiTypesAndPopulatedDeterministicTelemetry()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["DesignPreview:Enabled"] = "true" }).Build();
        var resources = new SampleResourceProvider(configuration).GetResources();
        var server = Assert.Single(resources, resource => resource.EffectiveTypeId == SqlServerResourceTypeProvider.ResourceTypeId.ToString());
        Assert.Equal(7, resources.Count(resource => resource.ParentResourceId == server.Id));
        var broker = Assert.Single(resources, resource => resource.EffectiveTypeId == RabbitMQResourceTypeProvider.ResourceTypeId.ToString());
        var snapshot = await new DesignPreviewBrokerDashboardProvider().GetDashboardAsync(broker);
        Assert.True(snapshot.Succeeded);
        Assert.Equal(6, snapshot.Queues.Count);
        Assert.Equal(30, snapshot.Throughput.Count);
        Assert.Equal(snapshot.Queues.Sum(queue => queue.Messages), snapshot.Totals.Messages);
        Assert.Equal(snapshot.Totals.Messages, snapshot.Totals.MessagesReady + snapshot.Totals.MessagesUnacknowledged);
    }

    [Fact]
    public void PreviewIsOptIn()
    {
        var configuration = new ConfigurationBuilder().Build();
        Assert.Equal(3, new SampleResourceProvider(configuration).GetResources().Count);
    }
}
