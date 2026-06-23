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
            TraceId: activity.TraceId.ToString(),
            SpanId: activity.SpanId.ToString())));
        Assert.Equal(activity.TraceId.ToString(), resourceEvent.TraceId);
        Assert.Equal(activity.SpanId.ToString(), resourceEvent.SpanId);
    }

    [Fact]
    public void InMemoryResourceEventStore_FiltersByEventTypeActorTimeRangeAndMaxEvents()
    {
        var store = new InMemoryResourceEventStore();
        var started = DateTimeOffset.Parse("2026-06-18T08:00:00+00:00");
        var updated = DateTimeOffset.Parse("2026-06-18T08:05:00+00:00");
        var stopped = DateTimeOffset.Parse("2026-06-18T08:10:00+00:00");

        store.Append(new ResourceEvent(
            "application:test",
            ResourceEventTypes.Events.Lifecycle.Started,
            "Resource started.",
            started,
            TriggeredBy: "operator"));
        store.Append(new ResourceEvent(
            "application:test",
            ResourceEventTypes.Events.Deployment.ImageUpdated,
            "Image updated.",
            updated,
            TriggeredBy: "build-server"));
        store.Append(new ResourceEvent(
            "application:test",
            ResourceEventTypes.Events.Lifecycle.Stopped,
            "Resource stopped.",
            stopped,
            TriggeredBy: "operator"));

        var events = store.GetEvents(new ResourceEventQuery(
            ResourceId: "application:test",
            TriggeredBy: "operator",
            Since: started,
            Before: stopped,
            MaxEvents: 1));

        var resourceEvent = Assert.Single(events);
        Assert.Equal(ResourceEventTypes.Events.Lifecycle.Started, resourceEvent.EventType);
        Assert.Equal("operator", resourceEvent.TriggeredBy);
    }

    [Fact]
    public void InMemoryResourceEventStore_ReturnsNewestEventsAcrossResources()
    {
        var store = new InMemoryResourceEventStore();

        store.Append(new ResourceEvent(
            "application:old",
            ResourceEventTypes.Events.Lifecycle.Started,
            "Old event.",
            DateTimeOffset.Parse("2026-06-18T08:00:00+00:00")));
        store.Append(new ResourceEvent(
            "application:new",
            ResourceEventTypes.Events.Lifecycle.Started,
            "New event.",
            DateTimeOffset.Parse("2026-06-18T08:05:00+00:00")));

        var resourceEvent = Assert.Single(store.GetEvents(new ResourceEventQuery(MaxEvents: 1)));

        Assert.Equal("application:new", resourceEvent.ResourceId);
    }

    [Fact]
    public void InMemoryResourceEventStore_PreservesAppendOrderForSameTimestamp()
    {
        var store = new InMemoryResourceEventStore();
        var timestamp = DateTimeOffset.Parse("2026-06-18T08:00:00+00:00");

        store.Append(new ResourceEvent(
            "application:test",
            ResourceEventTypes.Events.Deployment.Applying,
            "Applying deployment.",
            timestamp));
        store.Append(new ResourceEvent(
            "application:test",
            ResourceEventTypes.Events.Deployment.ReplicaMaterializing,
            "Materializing replica.",
            timestamp));
        store.Append(new ResourceEvent(
            "application:test",
            ResourceEventTypes.Events.Deployment.ReplicaMaterialized,
            "Materialized replica.",
            timestamp));

        var events = store
            .GetEvents(new ResourceEventQuery(ResourceId: "application:test"))
            .Reverse()
            .ToArray();

        Assert.Equal(
            [
                ResourceEventTypes.Events.Deployment.Applying,
                ResourceEventTypes.Events.Deployment.ReplicaMaterializing,
                ResourceEventTypes.Events.Deployment.ReplicaMaterialized
            ],
            events.Select(resourceEvent => resourceEvent.EventType).ToArray());
    }

    [Fact]
    public void ResourceEvent_PreservesExplicitTraceContext()
    {
        using var activity = new Activity("resource-event-test");
        activity.Start();

        var resourceEvent = new ResourceEvent(
                "application:test",
                ResourceEventTypes.Events.Lifecycle.Started,
                "Resource started.",
                DateTimeOffset.UtcNow,
                TraceId: "explicit-trace",
                SpanId: "explicit-span")
            .WithCurrentTraceContext();

        Assert.Equal("explicit-trace", resourceEvent.TraceId);
        Assert.Equal("explicit-span", resourceEvent.SpanId);
    }
}
