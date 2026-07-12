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
    public void ObservingResourceEventSink_CoalescesStartDeploymentMaterializationIntoLifecycleNotification()
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
            ResourceEventTypes.Events.Lifecycle.Starting,
            "Resource is starting.",
            DateTimeOffset.Parse("2026-07-11T09:00:00+00:00"),
            TriggeredBy: "operator"));
        sink.Append(new ResourceEvent(
            "application:api",
            ResourceEventTypes.Events.Deployment.Applying,
            "Applying deployment 'api-deployment' for revision 'revision-1'. Cause: Resource start requested runtime materialization.",
            DateTimeOffset.Parse("2026-07-11T09:00:01+00:00"),
            TriggeredBy: "operator"));
        sink.Append(new ResourceEvent(
            "application:api",
            ResourceEventTypes.Events.Deployment.ReplicaMaterializing,
            "Materializing replica 1/3 'api-replica-1' for deployment 'api-deployment'. Cause: Resource start requested runtime materialization.",
            DateTimeOffset.Parse("2026-07-11T09:00:02+00:00"),
            TriggeredBy: "operator"));
        sink.Append(new ResourceEvent(
            "application:api",
            ResourceEventTypes.Events.Deployment.RoutingUpdating,
            "Updating routing for orchestrator service 'api' to revision 'revision-1' for deployment 'api-deployment'. Cause: Resource start requested runtime materialization.",
            DateTimeOffset.Parse("2026-07-11T09:00:03+00:00"),
            TriggeredBy: "operator"));
        sink.Append(new ResourceEvent(
            "application:api",
            ResourceEventTypes.Events.Deployment.Applied,
            "Applied deployment 'api-deployment' for revision 'revision-1'. Result: Applied deployment 'api-deployment' for runtime revision 'revision-1'. Cause: Resource start requested runtime materialization.",
            DateTimeOffset.Parse("2026-07-11T09:00:04+00:00"),
            TriggeredBy: "operator"));
        sink.Append(new ResourceEvent(
            "application:api",
            ResourceEventTypes.Events.Lifecycle.Started,
            "Resource started.",
            DateTimeOffset.Parse("2026-07-11T09:00:05+00:00"),
            TriggeredBy: "operator",
            Severity: ResourceSignalSeverity.Success));

        Assert.Equal(6, resourceEvents.GetEvents(new ResourceEventQuery(ResourceId: "application:api")).Count);

        var notification = Assert.Single(notifications.GetNotifications(new CloudShellNotificationQuery(
            RecipientKey: "operator")));
        Assert.Equal("Start resource", notification.Title);
        Assert.Equal("Resource started.", notification.Message);
        Assert.Equal(CloudShellNotificationStatus.Succeeded, notification.Status);
        Assert.Equal(ResourceEventTypes.Events.Lifecycle.Started, notification.EventType);
        Assert.Equal("resource-lifecycle|application:api|start|operator", notification.CorrelationId);
        Assert.Equal("cloudshell.resource-lifecycle-operation", notification.TemplateKey);
        Assert.Equal("start", notification.Attributes!["actionId"]);
        Assert.Equal("lifecycle", notification.Attributes!["operationKind"]);
        Assert.Equal(6, changes.Count);
        Assert.Equal(CloudShellNotificationChangeKind.Created, changes[0]);
        Assert.All(changes.Skip(1), change => Assert.Equal(CloudShellNotificationChangeKind.Updated, change));
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
    public void ObservingResourceEventSink_CoalescesDeploymentUpdateProgressIntoOneNotification()
    {
        var resourceEvents = new InMemoryResourceEventStore();
        var notifications = new InMemoryCloudShellNotificationStore();
        var projector = new ResourceEventNotificationProjector(
            notifications,
            [new DefaultResourceEventNotificationRule()]);
        var sink = new ObservingResourceEventSink(resourceEvents, [projector]);

        sink.Append(new ResourceEvent(
            "application:api",
            ResourceEventTypes.Events.Deployment.ImageUpdating,
            "Updating image to 'example/api:2'.",
            DateTimeOffset.Parse("2026-07-11T09:00:00+00:00"),
            TriggeredBy: "operator"));
        sink.Append(new ResourceEvent(
            "application:api",
            ResourceEventTypes.Events.Deployment.ImageUpdated,
            "Updated image to 'example/api:2'.",
            DateTimeOffset.Parse("2026-07-11T09:00:01+00:00"),
            TriggeredBy: "operator",
            Severity: ResourceSignalSeverity.Success));

        var notification = Assert.Single(notifications.GetNotifications(new CloudShellNotificationQuery(
            RecipientKey: "operator")));
        Assert.Equal("Update resource image", notification.Title);
        Assert.Equal("Updated image to 'example/api:2'.", notification.Message);
        Assert.Equal(CloudShellNotificationStatus.Succeeded, notification.Status);
        Assert.Equal(ResourceEventTypes.Events.Deployment.ImageUpdated, notification.EventType);
        Assert.Equal("resource-update|application:api|image|operator", notification.CorrelationId);
        Assert.Equal("cloudshell.resource-update-operation", notification.TemplateKey);
        Assert.Equal("update", notification.Attributes!["operationKind"]);
        Assert.Equal("image", notification.Attributes!["updateKind"]);
    }

    [Fact]
    public void ObservingResourceEventSink_CoalescesDeploymentApplyProgressIntoOneNotification()
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
            ResourceEventTypes.Events.Deployment.Applying,
            "Applying deployment 'api-deployment' for revision 'revision-2'.",
            DateTimeOffset.Parse("2026-07-11T09:00:00+00:00"),
            TriggeredBy: "operator"));
        sink.Append(new ResourceEvent(
            "application:api",
            ResourceEventTypes.Events.Deployment.Applied,
            "Applied deployment 'api-deployment' for revision 'revision-2'.",
            DateTimeOffset.Parse("2026-07-11T09:00:01+00:00"),
            TriggeredBy: "operator"));

        var notification = Assert.Single(notifications.GetNotifications(new CloudShellNotificationQuery(
            RecipientKey: "operator")));
        Assert.Equal("Apply deployment", notification.Title);
        Assert.Equal("Applied deployment 'api-deployment' for revision 'revision-2'.", notification.Message);
        Assert.Equal(CloudShellNotificationStatus.Succeeded, notification.Status);
        Assert.Equal(ResourceEventTypes.Events.Deployment.Applied, notification.EventType);
        Assert.Equal("resource-deployment|application:api|apply|operator", notification.CorrelationId);
        Assert.Equal("cloudshell.deployment-apply-operation", notification.TemplateKey);
        Assert.Equal("deployment", notification.Attributes!["operationKind"]);
        Assert.Equal("apply", notification.Attributes!["deploymentKind"]);
        Assert.Equal(
            [
                CloudShellNotificationChangeKind.Created,
                CloudShellNotificationChangeKind.Updated
            ],
            changes);
    }

    [Fact]
    public void ObservingResourceEventSink_CoalescesRecoveryProgressIntoOneOperatorNotification()
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
            ResourceEventTypes.Events.Lifecycle.StoppedUnexpectedly,
            "Resource stopped unexpectedly.",
            DateTimeOffset.Parse("2026-07-11T09:00:00+00:00"),
            TriggeredBy: "liveness",
            Severity: ResourceSignalSeverity.Error));
        sink.Append(new ResourceEvent(
            "application:api",
            ResourceEventTypes.Events.Recovery.SignalFailed,
            "Recovery signal failed.",
            DateTimeOffset.Parse("2026-07-11T09:00:01+00:00"),
            TriggeredBy: "recovery",
            Severity: ResourceSignalSeverity.Warning));
        sink.Append(new ResourceEvent(
            "application:api",
            ResourceEventTypes.Events.Recovery.RestartAttempted,
            "Recovery restart attempt 1 started.",
            DateTimeOffset.Parse("2026-07-11T09:00:02+00:00"),
            TriggeredBy: "recovery",
            Severity: ResourceSignalSeverity.Warning));
        sink.Append(new ResourceEvent(
            "application:api",
            ResourceEventTypes.Events.Recovery.RestartSucceeded,
            "Recovery restart attempt 1 completed.",
            DateTimeOffset.Parse("2026-07-11T09:00:03+00:00"),
            TriggeredBy: "recovery",
            Severity: ResourceSignalSeverity.Success));

        Assert.Equal(4, resourceEvents.GetEvents(new ResourceEventQuery(ResourceId: "application:api")).Count);
        Assert.Empty(notifications.GetNotifications(new CloudShellNotificationQuery(RecipientKey: "liveness")));
        Assert.Empty(notifications.GetNotifications(new CloudShellNotificationQuery(RecipientKey: "recovery")));

        var notification = Assert.Single(notifications.GetNotifications(new CloudShellNotificationQuery(
            RecipientKey: "user")));
        Assert.Equal("Resource recovery", notification.Title);
        Assert.Equal("Recovery restart attempt 1 completed.", notification.Message);
        Assert.Equal(CloudShellNotificationStatus.Succeeded, notification.Status);
        Assert.Equal(ResourceEventTypes.Events.Recovery.RestartSucceeded, notification.EventType);
        Assert.Equal("resource-recovery|application:api|resource|user", notification.CorrelationId);
        Assert.Equal("cloudshell.resource-recovery-operation", notification.TemplateKey);
        Assert.Equal("recovery", notification.Attributes!["operationKind"]);
        Assert.Equal("resource", notification.Attributes!["recoveryKind"]);
        Assert.Equal("recovery", notification.Attributes!["triggeredBy"]);
        Assert.Equal(
            [
                CloudShellNotificationChangeKind.Created,
                CloudShellNotificationChangeKind.Updated,
                CloudShellNotificationChangeKind.Updated
            ],
            changes);
    }

    [Fact]
    public void ObservingResourceEventSink_CoalescesReplicaRepairProgressIntoOneOperatorNotification()
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
            ResourceEventTypes.Events.ReplicaManagement.SlotUnhealthy,
            "Replica group slot 2/3 is unhealthy.",
            DateTimeOffset.Parse("2026-07-11T09:00:00+00:00"),
            TriggeredBy: "replica-management",
            Severity: ResourceSignalSeverity.Warning));
        sink.Append(new ResourceEvent(
            "application:api",
            ResourceEventTypes.Events.ReplicaManagement.ReconciliationDeferred,
            "Replica slot has 1/2 unhealthy observations before repair.",
            DateTimeOffset.Parse("2026-07-11T09:00:01+00:00"),
            TriggeredBy: "replica-management",
            Severity: ResourceSignalSeverity.Warning));
        sink.Append(new ResourceEvent(
            "application:api",
            ResourceEventTypes.Events.ReplicaManagement.OccupantCrashed,
            "Replica group slot 2/3 occupant is not healthy.",
            DateTimeOffset.Parse("2026-07-11T09:00:02+00:00"),
            TriggeredBy: "replica-management",
            Severity: ResourceSignalSeverity.Warning));
        sink.Append(new ResourceEvent(
            "application:api",
            ResourceEventTypes.Events.ReplicaManagement.ReplacementScheduled,
            "Replica group slot 2/3 replacement was scheduled.",
            DateTimeOffset.Parse("2026-07-11T09:00:03+00:00"),
            TriggeredBy: "replica-management",
            Severity: ResourceSignalSeverity.Warning));
        sink.Append(new ResourceEvent(
            "application:api",
            ResourceEventTypes.Events.ReplicaManagement.ReplacementMaterializing,
            "Replica group slot 2/3 replacement is materializing.",
            DateTimeOffset.Parse("2026-07-11T09:00:04+00:00"),
            TriggeredBy: "replica-management",
            Severity: ResourceSignalSeverity.Warning));
        sink.Append(new ResourceEvent(
            "application:api",
            ResourceEventTypes.Events.ReplicaManagement.ReplacementMaterialized,
            "Replica group slot 2/3 replacement materialized.",
            DateTimeOffset.Parse("2026-07-11T09:00:05+00:00"),
            TriggeredBy: "replica-management",
            Severity: ResourceSignalSeverity.Success));

        Assert.Equal(6, resourceEvents.GetEvents(new ResourceEventQuery(ResourceId: "application:api")).Count);
        Assert.Empty(notifications.GetNotifications(new CloudShellNotificationQuery(RecipientKey: "replica-management")));

        var notification = Assert.Single(notifications.GetNotifications(new CloudShellNotificationQuery(
            RecipientKey: "user")));
        Assert.Equal("Replica repair", notification.Title);
        Assert.Equal("Replica group slot 2/3 replacement materialized.", notification.Message);
        Assert.Equal(CloudShellNotificationStatus.Succeeded, notification.Status);
        Assert.Equal(ResourceEventTypes.Events.ReplicaManagement.ReplacementMaterialized, notification.EventType);
        Assert.Equal("resource-replica-repair|application:api|replica|user", notification.CorrelationId);
        Assert.Equal("cloudshell.replica-repair-operation", notification.TemplateKey);
        Assert.Equal("replicaRepair", notification.Attributes!["operationKind"]);
        Assert.Equal("replica", notification.Attributes!["repairKind"]);
        Assert.Equal("replica-management", notification.Attributes!["triggeredBy"]);
        Assert.Equal(
            [
                CloudShellNotificationChangeKind.Created,
                CloudShellNotificationChangeKind.Updated,
                CloudShellNotificationChangeKind.Updated,
                CloudShellNotificationChangeKind.Updated
            ],
            changes);
    }

    [Fact]
    public void DefaultResourceEventNotificationRule_MapsRecoveryExhaustionToFailedOperatorNotification()
    {
        var rule = new DefaultResourceEventNotificationRule();

        var notification = rule.CreateNotification(new ResourceEvent(
            "application:api",
            ResourceEventTypes.Events.Recovery.RestartExhausted,
            "Maximum recovery attempts reached.",
            DateTimeOffset.UtcNow,
            TriggeredBy: "recovery",
            Severity: ResourceSignalSeverity.Error));

        Assert.NotNull(notification);
        Assert.Equal("user", notification!.RecipientKey);
        Assert.Equal("Resource recovery", notification.Title);
        Assert.Equal(CloudShellNotificationStatus.Failed, notification.Status);
        Assert.Equal("resource-recovery|application:api|resource|user", notification.CorrelationId);
        Assert.Equal("cloudshell.resource-recovery-operation", notification.TemplateKey);
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
            ResourceEventTypes.Events.Lifecycle.Starting,
            "Resource is starting.",
            DateTimeOffset.UtcNow,
            TriggeredBy: "operator"));
        var success = rule.CreateNotification(new ResourceEvent(
            "application:api",
            ResourceEventTypes.Events.Lifecycle.Started,
            "Resource started.",
            DateTimeOffset.UtcNow,
            TriggeredBy: "operator"));
        var failure = rule.CreateNotification(new ResourceEvent(
            "application:api",
            ResourceEventTypes.Events.Lifecycle.StartFailed,
            "Resource failed to start.",
            DateTimeOffset.UtcNow,
            TriggeredBy: "operator"));

        Assert.Equal(CloudShellNotificationStatus.InProgress, progress!.Status);
        Assert.Equal(CloudShellNotificationStatus.Succeeded, success!.Status);
        Assert.Equal(CloudShellNotificationStatus.Failed, failure!.Status);
    }

    [Fact]
    public void DefaultResourceEventNotificationRule_AddsActionsForExistingResourceFailures()
    {
        var rule = new DefaultResourceEventNotificationRule();

        var lifecycleFailure = rule.CreateNotification(new ResourceEvent(
            "application:api",
            ResourceEventTypes.Events.Lifecycle.StartFailed,
            "Resource failed to start.",
            DateTimeOffset.UtcNow,
            TriggeredBy: "operator",
            Severity: ResourceSignalSeverity.Error));
        var createFailure = rule.CreateNotification(new ResourceEvent(
            "application:api",
            ResourceEventTypes.Events.Resource.CreateFailed,
            "Resource failed to create.",
            DateTimeOffset.UtcNow,
            TriggeredBy: "operator",
            Severity: ResourceSignalSeverity.Error));

        Assert.NotNull(lifecycleFailure!.Actions);
        var actions = lifecycleFailure.Actions;
        Assert.Collection(
            actions,
            action =>
            {
                Assert.Equal("open-resource", action.Id);
                Assert.Equal("Open resource", action.Label);
                Assert.True(action.IsPrimary);
                Assert.Equal("/resources/application%3Aapi", action.Target!.Href);
            },
            action =>
            {
                Assert.Equal("view-activity", action.Id);
                Assert.Equal("View activity", action.Label);
                Assert.False(action.IsPrimary);
                Assert.Equal("/resources/application%3Aapi/activity", action.Target!.Href);
            });
        Assert.Null(createFailure!.Actions);
    }

    [Fact]
    public void DefaultResourceEventNotificationRule_MapsContainerAppRuntimeFailureAndRecoveryStatus()
    {
        var rule = new DefaultResourceEventNotificationRule();

        var degraded = rule.CreateNotification(new ResourceEvent(
            "application:api",
            ResourceEventTypes.Events.Lifecycle.Degraded,
            "Resource degraded.",
            DateTimeOffset.UtcNow,
            TriggeredBy: "liveness",
            Severity: ResourceSignalSeverity.Warning));
        var stoppedUnexpectedly = rule.CreateNotification(new ResourceEvent(
            "application:api",
            ResourceEventTypes.Events.Lifecycle.StoppedUnexpectedly,
            "Resource stopped unexpectedly.",
            DateTimeOffset.UtcNow,
            TriggeredBy: "liveness",
            Severity: ResourceSignalSeverity.Error));
        var restartAttempted = rule.CreateNotification(new ResourceEvent(
            "application:api",
            ResourceEventTypes.Events.ReplicaManagement.RestartAttempted,
            "Replica restart started.",
            DateTimeOffset.UtcNow,
            TriggeredBy: "replica-management",
            Severity: ResourceSignalSeverity.Warning));
        var restartSucceeded = rule.CreateNotification(new ResourceEvent(
            "application:api",
            ResourceEventTypes.Events.ReplicaManagement.RestartSucceeded,
            "Replica restarted.",
            DateTimeOffset.UtcNow,
            TriggeredBy: "replica-management",
            Severity: ResourceSignalSeverity.Success));
        var restartExhausted = rule.CreateNotification(new ResourceEvent(
            "application:api",
            ResourceEventTypes.Events.ReplicaManagement.ReconciliationExhausted,
            "Replica repair exhausted.",
            DateTimeOffset.UtcNow,
            TriggeredBy: "replica-management",
            Severity: ResourceSignalSeverity.Error));

        Assert.Equal(CloudShellNotificationStatus.NeedsAttention, degraded!.Status);
        Assert.Equal(CloudShellNotificationStatus.NeedsAttention, stoppedUnexpectedly!.Status);
        Assert.Equal(CloudShellNotificationStatus.InProgress, restartAttempted!.Status);
        Assert.Equal(CloudShellNotificationStatus.Succeeded, restartSucceeded!.Status);
        Assert.Equal(CloudShellNotificationStatus.Failed, restartExhausted!.Status);
        Assert.All(
            [degraded, stoppedUnexpectedly, restartAttempted, restartSucceeded, restartExhausted],
            notification =>
            {
                Assert.Equal("user", notification!.RecipientKey);
            });
        Assert.Equal("Resource recovery", degraded!.Title);
        Assert.Equal("Resource recovery", stoppedUnexpectedly!.Title);
        Assert.Equal("Replica repair", restartAttempted!.Title);
        Assert.Equal("Replica repair", restartSucceeded!.Title);
        Assert.Equal("Replica repair", restartExhausted!.Title);
        Assert.Equal("resource-recovery|application:api|resource|user", degraded.CorrelationId);
        Assert.Equal("resource-recovery|application:api|resource|user", stoppedUnexpectedly.CorrelationId);
        Assert.Equal("resource-replica-repair|application:api|replica|user", restartAttempted.CorrelationId);
        Assert.Equal("resource-replica-repair|application:api|replica|user", restartSucceeded.CorrelationId);
        Assert.Equal("resource-replica-repair|application:api|replica|user", restartExhausted.CorrelationId);
    }

    [Fact]
    public void DefaultResourceEventNotificationRule_SuppressesUnhandledTriggeredEvents()
    {
        var rule = new DefaultResourceEventNotificationRule();

        var notification = rule.CreateNotification(new ResourceEvent(
            "application:api",
            ResourceEventTypes.Events.Configuration.AppSettingsUpdated,
            "Application settings updated.",
            DateTimeOffset.UtcNow,
            TriggeredBy: "operator"));

        Assert.Null(notification);
    }
}
