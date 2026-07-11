using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.Notifications;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Logs;
using CloudShell.ControlPlane.Notifications;

namespace CloudShell.ControlPlane.Tests;

public sealed class CloudShellNotificationStoreTests
{
    [Fact]
    public void InMemoryStore_CreatesAcknowledgesAndDismissesNotifications()
    {
        var store = new InMemoryCloudShellNotificationStore();
        var changes = new List<CloudShellNotificationChangeKind>();
        store.NotificationsChanged += (_, args) => changes.Add(args.Kind);

        var notification = store.CreateNotification(new CreateCloudShellNotificationCommand(
            "operator",
            "Resource started",
            "The resource started.",
            ResourceSignalSeverity.Success,
            CloudShellNotificationStatus.Succeeded,
            ResourceId: "application:api",
            Actions:
            [
                new CloudShellNotificationAction(
                    "open",
                    "Open resource",
                    new CloudShellNotificationTarget("/resources/application%3Aapi", "Open"),
                    IsPrimary: true)
            ]));

        Assert.NotEmpty(notification.Id);
        Assert.Equal("operator", notification.RecipientKey);
        Assert.Equal("application:api", notification.ResourceId);
        Assert.Same(notification, store.GetNotification(notification.Id));
        var action = Assert.Single(notification.Actions!);
        Assert.Equal("open", action.Id);
        Assert.True(action.IsPrimary);
        Assert.Equal("/resources/application%3Aapi", action.Target!.Href);

        Assert.True(store.AcknowledgeNotification(notification.Id));
        Assert.True(store.DismissNotification(notification.Id));
        Assert.False(store.AcknowledgeNotification("missing"));

        Assert.Empty(store.GetNotifications(new CloudShellNotificationQuery(RecipientKey: "operator")));

        var dismissed = Assert.Single(store.GetNotifications(new CloudShellNotificationQuery(
            RecipientKey: "operator",
            IncludeDismissed: true)));
        Assert.NotNull(dismissed.AcknowledgedAt);
        Assert.NotNull(dismissed.DismissedAt);
        Assert.Equal(
            [
                CloudShellNotificationChangeKind.Created,
                CloudShellNotificationChangeKind.Acknowledged,
                CloudShellNotificationChangeKind.Dismissed
            ],
            changes);
    }

    [Fact]
    public void InMemoryStore_UpdatesExistingNotificationByRecipientAndCorrelation()
    {
        var store = new InMemoryCloudShellNotificationStore();
        var changes = new List<CloudShellNotificationChangeKind>();
        store.NotificationsChanged += (_, args) => changes.Add(args.Kind);

        var progress = store.CreateOrUpdateNotification(new CreateCloudShellNotificationCommand(
            "operator",
            "Start resource",
            "Resource is starting.",
            ResourceSignalSeverity.Info,
            CloudShellNotificationStatus.InProgress,
            ResourceId: "application:api",
            CorrelationId: "resource-lifecycle|application:api|start|operator"));

        var completed = store.CreateOrUpdateNotification(new CreateCloudShellNotificationCommand(
            "operator",
            "Start resource",
            "Resource started.",
            ResourceSignalSeverity.Success,
            CloudShellNotificationStatus.Succeeded,
            ResourceId: "application:api",
            EventType: ResourceEventTypes.Events.Lifecycle.Started,
            CorrelationId: "resource-lifecycle|application:api|start|operator"));

        Assert.Equal(progress.Id, completed.Id);
        var notification = Assert.Single(store.GetNotifications(new CloudShellNotificationQuery(
            RecipientKey: "operator")));
        Assert.Equal("Resource started.", notification.Message);
        Assert.Equal(CloudShellNotificationStatus.Succeeded, notification.Status);
        Assert.Equal(ResourceEventTypes.Events.Lifecycle.Started, notification.EventType);
        Assert.Equal(
            [
                CloudShellNotificationChangeKind.Created,
                CloudShellNotificationChangeKind.Updated
            ],
            changes);
    }

    [Fact]
    public void ObservingResourceEventSink_ProjectsTriggeredResourceEventsToNotifications()
    {
        var resourceEvents = new InMemoryResourceEventStore();
        var notifications = new InMemoryCloudShellNotificationStore();
        var projector = new ResourceEventNotificationProjector(
            notifications,
            [new DefaultResourceEventNotificationRule()]);
        var sink = new ObservingResourceEventSink(resourceEvents, [projector]);

        sink.Append(new ResourceEvent(
            "application:api",
            ResourceEventTypes.Events.Lifecycle.Started,
            "Resource started.",
            DateTimeOffset.Parse("2026-07-11T09:00:00+00:00"),
            TriggeredBy: "operator",
            Severity: ResourceSignalSeverity.Success,
            TraceId: "trace-1",
            SpanId: "span-1"));

        Assert.Single(resourceEvents.GetEvents(new ResourceEventQuery(ResourceId: "application:api")));

        var notification = Assert.Single(notifications.GetNotifications(new CloudShellNotificationQuery(
            RecipientKey: "operator")));
        Assert.Equal("Resource started.", notification.Message);
        Assert.Equal(ResourceSignalSeverity.Success, notification.Severity);
        Assert.Equal(CloudShellNotificationStatus.Succeeded, notification.Status);
        Assert.Equal("Control Plane", notification.Source);
        Assert.Equal("application:api", notification.ResourceId);
        Assert.Equal(ResourceEventTypes.Events.Lifecycle.Started, notification.EventType);
        Assert.Equal("resource-lifecycle|application:api|start|operator", notification.CorrelationId);
        Assert.Equal("cloudshell.resource-lifecycle-operation", notification.TemplateKey);
        Assert.NotEmpty(notification.EventId!);
        Assert.Equal("trace-1", notification.Attributes!["traceId"]);
        Assert.Equal("span-1", notification.Attributes!["spanId"]);
        Assert.Equal("start", notification.Attributes!["actionId"]);
    }

    [Fact]
    public void ObservingResourceEventSink_CoalescesLifecycleProgressIntoOneNotification()
    {
        var resourceEvents = new InMemoryResourceEventStore();
        var notifications = new InMemoryCloudShellNotificationStore();
        var changes = new List<CloudShellNotificationChangeKind>();
        notifications.NotificationsChanged += (_, args) => changes.Add(args.Kind);
        var projector = new ResourceEventNotificationProjector(
            notifications,
            [new DefaultResourceEventNotificationRule()]);
        var sink = new ObservingResourceEventSink(resourceEvents, [projector]);

        sink.Append(new ResourceEvent(
            "application:api",
            ResourceEventTypes.Actions.Lifecycle.Start,
            "Start was requested.",
            DateTimeOffset.Parse("2026-07-11T09:00:00+00:00"),
            TriggeredBy: "operator"));
        sink.Append(new ResourceEvent(
            "application:api",
            ResourceEventTypes.Events.Lifecycle.Starting,
            "Resource is starting.",
            DateTimeOffset.Parse("2026-07-11T09:00:01+00:00"),
            TriggeredBy: "operator"));
        sink.Append(new ResourceEvent(
            "application:api",
            ResourceEventTypes.Events.Lifecycle.Started,
            "Resource started.",
            DateTimeOffset.Parse("2026-07-11T09:00:02+00:00"),
            TriggeredBy: "operator",
            Severity: ResourceSignalSeverity.Success));

        Assert.Equal(3, resourceEvents.GetEvents(new ResourceEventQuery(ResourceId: "application:api")).Count);

        var notification = Assert.Single(notifications.GetNotifications(new CloudShellNotificationQuery(
            RecipientKey: "operator")));
        Assert.Equal("Start resource", notification.Title);
        Assert.Equal("Resource started.", notification.Message);
        Assert.Equal(CloudShellNotificationStatus.Succeeded, notification.Status);
        Assert.Equal(ResourceEventTypes.Events.Lifecycle.Started, notification.EventType);
        Assert.Equal("start", notification.Attributes!["actionId"]);
        Assert.Equal("lifecycle", notification.Attributes!["operationKind"]);
        Assert.Equal(
            [
                CloudShellNotificationChangeKind.Created,
                CloudShellNotificationChangeKind.Updated
            ],
            changes);
    }

    [Fact]
    public void ObservingResourceEventSink_CoalescesResourceCreateProgressIntoOneNotification()
    {
        var resourceEvents = new InMemoryResourceEventStore();
        var notifications = new InMemoryCloudShellNotificationStore();
        var projector = new ResourceEventNotificationProjector(
            notifications,
            [new DefaultResourceEventNotificationRule()]);
        var sink = new ObservingResourceEventSink(resourceEvents, [projector]);

        sink.Append(new ResourceEvent(
            "application:api",
            ResourceEventTypes.Events.Resource.Creating,
            "Creating resource 'API'.",
            DateTimeOffset.Parse("2026-07-11T09:00:00+00:00"),
            TriggeredBy: "operator"));
        sink.Append(new ResourceEvent(
            "application:api",
            ResourceEventTypes.Events.Resource.Created,
            "Resource 'API' created.",
            DateTimeOffset.Parse("2026-07-11T09:00:01+00:00"),
            TriggeredBy: "operator",
            Severity: ResourceSignalSeverity.Success));

        var notification = Assert.Single(notifications.GetNotifications(new CloudShellNotificationQuery(
            RecipientKey: "operator")));
        Assert.Equal("Create resource", notification.Title);
        Assert.Equal("Resource 'API' created.", notification.Message);
        Assert.Equal(CloudShellNotificationStatus.Succeeded, notification.Status);
        Assert.Equal(ResourceEventTypes.Events.Resource.Created, notification.EventType);
        Assert.Equal("resource-create|application:api|create|operator", notification.CorrelationId);
        Assert.Equal("cloudshell.resource-create-operation", notification.TemplateKey);
        Assert.Equal("create", notification.Attributes!["operationKind"]);
    }

    [Fact]
    public void DefaultResourceEventNotificationRule_RequiresTriggeredByRecipient()
    {
        var rule = new DefaultResourceEventNotificationRule();

        var notification = rule.CreateNotification(new ResourceEvent(
            "application:api",
            ResourceEventTypes.Events.Lifecycle.Started,
            "Resource started.",
            DateTimeOffset.UtcNow));

        Assert.Null(notification);
    }

    [Fact]
    public void DefaultResourceEventNotificationRule_MapsProgressAndFailureStatus()
    {
        var rule = new DefaultResourceEventNotificationRule();

        var progress = rule.CreateNotification(new ResourceEvent(
            "application:api",
            ResourceEventTypes.Events.Deployment.Applying,
            "Applying deployment.",
            DateTimeOffset.UtcNow,
            TriggeredBy: "operator"));
        var failure = rule.CreateNotification(new ResourceEvent(
            "application:api",
            ResourceEventTypes.Events.Deployment.Failed,
            "Deployment failed.",
            DateTimeOffset.UtcNow,
            TriggeredBy: "operator",
            Severity: ResourceSignalSeverity.Error));

        Assert.Equal(CloudShellNotificationStatus.InProgress, progress!.Status);
        Assert.Equal(CloudShellNotificationStatus.Failed, failure!.Status);
    }
}
