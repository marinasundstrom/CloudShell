using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Providers;

namespace CloudShell.ResourceHost;

/// <summary>Deterministic UI fixture, registered only with DesignPreview:Enabled.</summary>
public sealed class DesignPreviewBrokerDashboardProvider : IRabbitMQBrokerDashboardProvider
{
    public Task<RabbitMQBrokerDashboardResult> GetDashboardAsync(Resource resource, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        RabbitMQQueueDashboardInfo[] queues =
        [
            new("orders.awaiting.fulfillment.international.priority.with.extended.routing.name", "/", "running", 124800, 120000, 4800, 12, 240, 220, 218),
            new("orders.deadletters", "/", "running", 12500, 12500, 0, 0, 3, 0, 0),
            new("notifications.email", "/", "running", 640, 600, 40, 4, 80, 80, 78),
            new("billing", "/", "running", 80, 20, 60, 2, 25, 25, 24),
            new("idle.queue", "/", "running", 0, 0, 0, 0, 0, 0, 0),
            new("audit", "/", "running", 10, 10, 0, 1, 2, 2, 2)
        ];
        var throughput = Enumerable.Range(0, 30).Select(i => new RabbitMQBrokerThroughputSample(
            now.AddSeconds((i - 29) * 5), 300 + 50 * Math.Sin(i * .4), 280 + 40 * Math.Cos(i * .3), 275 + 35 * Math.Cos(i * .3))).ToArray();
        return Task.FromResult(new RabbitMQBrokerDashboardResult("/",
            new(queues.Length, 4, 8, 16, queues.Sum(q => q.Consumers), queues.Sum(q => q.Messages),
                queues.Sum(q => q.MessagesReady), queues.Sum(q => q.MessagesUnacknowledged), 350, 320, 318),
            throughput, queues, now));
    }
}
