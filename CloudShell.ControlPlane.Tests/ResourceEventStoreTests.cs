using System.Diagnostics;
using CloudShell.Abstractions.Logs;
using CloudShell.ControlPlane.Logs;

namespace CloudShell.ControlPlane.Tests;

public sealed class ResourceEventStoreTests
{
    [Fact]
    public void InMemoryResourceEventStore_CapturesCurrentTraceContext()
    {
        using var activity = new Activity("resource-event-test");
        activity.Start();
        var store = new InMemoryResourceEventStore();

        store.Append(new ResourceEvent(
            "application:test",
            ResourceEventTypes.Events.Lifecycle.Started,
            "Resource started.",
            DateTimeOffset.UtcNow));

        var resourceEvent = Assert.Single(store.GetEvents(new ResourceEventQuery(
            ResourceId: "application:test",
            TraceId: activity.TraceId.ToString())));
        Assert.Equal(activity.TraceId.ToString(), resourceEvent.TraceId);
        Assert.Equal(activity.SpanId.ToString(), resourceEvent.SpanId);
    }
}
