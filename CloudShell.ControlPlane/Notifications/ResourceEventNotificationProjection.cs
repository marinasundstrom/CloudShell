using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.Notifications;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.Notifications;

public sealed class ResourceEventNotificationProjector(
    ICloudShellNotificationStore notifications,
    IEnumerable<IResourceEventNotificationRule> rules) : IResourceEventObserver
{
    private readonly IReadOnlyList<IResourceEventNotificationRule> rules = rules.ToArray();

    public void OnResourceEvent(ResourceEvent resourceEvent)
    {
        foreach (var rule in rules)
        {
            var notification = rule.CreateNotification(resourceEvent);
            if (notification is not null)
            {
                notifications.CreateOrUpdateNotification(notification);
            }
        }
    }
}

public sealed class DefaultResourceEventNotificationRule : IResourceEventNotificationRule
{
    private const string LocalSystemNotificationRecipientKey = "user";
    private const string ResourceStartMaterializationCause = "Resource start requested runtime materialization";
    private const string OpenResourceActionId = "open-resource";
    private const string ViewActivityActionId = "view-activity";

    public CreateCloudShellNotificationCommand? CreateNotification(ResourceEvent resourceEvent)
    {
        var recipientKey = ResolveRecipientKey(resourceEvent);
        if (recipientKey is null)
        {
            return null;
        }

        if (resourceEvent.EventType.StartsWith(ResourceEventTypes.Actions.Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var operation = GetOperation(resourceEvent);
        if (operation is null)
        {
            return null;
        }

        var status = CreateStatus(resourceEvent);
        return new CreateCloudShellNotificationCommand(
            recipientKey,
            CreateTitle(resourceEvent),
            resourceEvent.Message,
            resourceEvent.Severity,
            status,
            Source: "Control Plane",
            ResourceId: resourceEvent.ResourceId,
            EventType: resourceEvent.EventType,
            EventId: CreateEventId(resourceEvent),
            CorrelationId: CreateOperationCorrelationId(resourceEvent, operation.Kind, operation.Id, recipientKey),
            TemplateKey: operation.TemplateKey,
            Actions: CreateResourceActions(resourceEvent, status),
            Attributes: CreateAttributes(resourceEvent));
    }

    private static CloudShellNotificationStatus CreateStatus(ResourceEvent resourceEvent)
    {
        var operationStatus = CreateOperationStatus(resourceEvent);
        if (operationStatus is not null)
        {
            return operationStatus.Value;
        }

        return resourceEvent.Severity switch
        {
            ResourceSignalSeverity.Success => CloudShellNotificationStatus.Succeeded,
            ResourceSignalSeverity.Warning => CloudShellNotificationStatus.NeedsAttention,
            ResourceSignalSeverity.Error => CloudShellNotificationStatus.Failed,
            _ when IsProgressEvent(resourceEvent.EventType) => CloudShellNotificationStatus.InProgress,
            _ => CloudShellNotificationStatus.Active
        };
    }

    private static CloudShellNotificationStatus? CreateOperationStatus(ResourceEvent resourceEvent)
    {
        if (IsStartMaterializationDeploymentEvent(resourceEvent))
        {
            return resourceEvent.EventType.Trim() == ResourceEventTypes.Events.Deployment.Failed
                ? CloudShellNotificationStatus.Failed
                : CloudShellNotificationStatus.InProgress;
        }

        return resourceEvent.EventType.Trim() switch
        {
            ResourceEventTypes.Events.Lifecycle.Starting or
            ResourceEventTypes.Events.Lifecycle.Stopping or
            ResourceEventTypes.Events.Lifecycle.Pausing or
            ResourceEventTypes.Events.Lifecycle.Restarting or
            ResourceEventTypes.Events.Resource.Creating or
            ResourceEventTypes.Events.Deployment.Applying or
            ResourceEventTypes.Events.Deployment.ImageUpdating or
            ResourceEventTypes.Events.Deployment.ReplicasUpdating => CloudShellNotificationStatus.InProgress,
            ResourceEventTypes.Events.Lifecycle.Started or
            ResourceEventTypes.Events.Lifecycle.Stopped or
            ResourceEventTypes.Events.Lifecycle.Paused or
            ResourceEventTypes.Events.Lifecycle.Restarted or
            ResourceEventTypes.Events.Resource.Created or
            ResourceEventTypes.Events.Deployment.Applied or
            ResourceEventTypes.Events.Deployment.ImageUpdated or
            ResourceEventTypes.Events.Deployment.ReplicasUpdated => CloudShellNotificationStatus.Succeeded,
            ResourceEventTypes.Events.Lifecycle.StartFailed or
            ResourceEventTypes.Events.Lifecycle.StopFailed or
            ResourceEventTypes.Events.Lifecycle.PauseFailed or
            ResourceEventTypes.Events.Lifecycle.RestartFailed or
            ResourceEventTypes.Events.Resource.CreateFailed or
            ResourceEventTypes.Events.Deployment.Failed or
            ResourceEventTypes.Events.Deployment.ImageUpdateFailed or
            ResourceEventTypes.Events.Deployment.ReplicasUpdateFailed => CloudShellNotificationStatus.Failed,
            ResourceEventTypes.Events.Recovery.RestartScheduled or
            ResourceEventTypes.Events.Recovery.RestartAttempted or
            ResourceEventTypes.Events.ReplicaManagement.RestartScheduled or
            ResourceEventTypes.Events.ReplicaManagement.RestartAttempted or
            ResourceEventTypes.Events.ReplicaManagement.ReplacementScheduled or
            ResourceEventTypes.Events.ReplicaManagement.ReplacementMaterializing => CloudShellNotificationStatus.InProgress,
            ResourceEventTypes.Events.Recovery.RestartSucceeded or
            ResourceEventTypes.Events.ReplicaManagement.RestartSucceeded or
            ResourceEventTypes.Events.ReplicaManagement.ReplacementMaterialized => CloudShellNotificationStatus.Succeeded,
            ResourceEventTypes.Events.Recovery.RestartFailed or
            ResourceEventTypes.Events.Recovery.RestartExhausted or
            ResourceEventTypes.Events.ReplicaManagement.ReconciliationFailed or
            ResourceEventTypes.Events.ReplicaManagement.ReconciliationExhausted => CloudShellNotificationStatus.Failed,
            ResourceEventTypes.Events.Lifecycle.Degraded or
            ResourceEventTypes.Events.Lifecycle.StoppedUnexpectedly or
            ResourceEventTypes.Events.Recovery.RestartSkipped or
            ResourceEventTypes.Events.ReplicaManagement.OccupantCrashed or
            ResourceEventTypes.Events.ReplicaManagement.SlotLeftVacant => CloudShellNotificationStatus.NeedsAttention,
            _ => null
        };
    }

    private static bool IsProgressEvent(string eventType)
    {
        var normalized = eventType.Trim().ToLowerInvariant();
        return normalized.EndsWith(".starting", StringComparison.Ordinal)
            || normalized.EndsWith(".creating", StringComparison.Ordinal)
            || normalized.EndsWith(".stopping", StringComparison.Ordinal)
            || normalized.EndsWith(".pausing", StringComparison.Ordinal)
            || normalized.EndsWith(".restarting", StringComparison.Ordinal)
            || normalized.EndsWith(".applying", StringComparison.Ordinal)
            || normalized.EndsWith(".running", StringComparison.Ordinal)
            || normalized.EndsWith(".updating", StringComparison.Ordinal)
            || normalized.EndsWith(".materializing", StringComparison.Ordinal)
            || normalized.EndsWith(".reconciling", StringComparison.Ordinal);
    }

    private static string CreateTitle(ResourceEvent resourceEvent)
    {
        var lifecycleActionId = GetLifecycleActionId(resourceEvent.EventType);
        if (lifecycleActionId is not null ||
            IsStartMaterializationDeploymentEvent(resourceEvent))
        {
            return $"{GetLifecycleActionLabel(lifecycleActionId ?? ResourceActionIds.Start)} resource";
        }

        if (IsResourceCreateEvent(resourceEvent.EventType))
        {
            return "Create resource";
        }

        var updateKind = GetDeploymentUpdateKind(resourceEvent.EventType);
        if (updateKind is not null)
        {
            return updateKind == "image"
                ? "Update resource image"
                : "Update resource replicas";
        }

        if (IsDeploymentApplyEvent(resourceEvent.EventType))
        {
            return "Apply deployment";
        }

        if (IsResourceRecoveryNotificationEvent(resourceEvent.EventType))
        {
            return "Resource recovery";
        }

        if (IsReplicaRepairNotificationEvent(resourceEvent.EventType))
        {
            return "Replica repair";
        }

        var name = resourceEvent.EventType
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault() ?? "event";

        return $"Resource {name}";
    }

    private static string CreateEventId(ResourceEvent resourceEvent)
    {
        var basis = string.Join(
            "|",
            resourceEvent.ResourceId,
            resourceEvent.EventType,
            resourceEvent.Timestamp.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
            resourceEvent.TriggeredBy ?? string.Empty,
            resourceEvent.TraceId ?? string.Empty,
            resourceEvent.SpanId ?? string.Empty);

        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(basis)))
            .ToLowerInvariant();
    }

    private static IReadOnlyList<CloudShellNotificationAction>? CreateResourceActions(
        ResourceEvent resourceEvent,
        CloudShellNotificationStatus status)
    {
        if (status is not (CloudShellNotificationStatus.Failed or CloudShellNotificationStatus.NeedsAttention) ||
            IsResourceCreateEvent(resourceEvent.EventType))
        {
            return null;
        }

        return
        [
            new CloudShellNotificationAction(
                OpenResourceActionId,
                "Open resource",
                new CloudShellNotificationTarget(
                    ResourceManagerRoutes.ResourceOverview(resourceEvent.ResourceId),
                    "Open resource"),
                IsPrimary: true),
            new CloudShellNotificationAction(
                ViewActivityActionId,
                "View activity",
                new CloudShellNotificationTarget(
                    ResourceManagerRoutes.ResourceDetails(
                        resourceEvent.ResourceId,
                        ResourcePredefinedViewIds.Activity),
                    "View activity"))
        ];
    }

    private static IReadOnlyDictionary<string, string> CreateAttributes(ResourceEvent resourceEvent)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["resourceId"] = resourceEvent.ResourceId,
            ["eventType"] = resourceEvent.EventType
        };

        var lifecycleActionId = GetLifecycleActionId(resourceEvent.EventType);
        if (lifecycleActionId is not null ||
            IsStartMaterializationDeploymentEvent(resourceEvent))
        {
            attributes["actionId"] = lifecycleActionId ?? ResourceActionIds.Start;
            attributes["operationKind"] = "lifecycle";
        }
        else if (IsResourceCreateEvent(resourceEvent.EventType))
        {
            attributes["operationKind"] = "create";
        }
        else if (GetDeploymentUpdateKind(resourceEvent.EventType) is { } updateKind)
        {
            attributes["operationKind"] = "update";
            attributes["updateKind"] = updateKind;
        }
        else if (IsDeploymentApplyEvent(resourceEvent.EventType))
        {
            attributes["operationKind"] = "deployment";
            attributes["deploymentKind"] = "apply";
        }
        else if (IsResourceRecoveryNotificationEvent(resourceEvent.EventType))
        {
            attributes["operationKind"] = "recovery";
            attributes["recoveryKind"] = "resource";
        }
        else if (IsReplicaRepairNotificationEvent(resourceEvent.EventType))
        {
            attributes["operationKind"] = "replicaRepair";
            attributes["repairKind"] = "replica";
        }

        if (!string.IsNullOrWhiteSpace(resourceEvent.TriggeredBy))
        {
            attributes["triggeredBy"] = resourceEvent.TriggeredBy.Trim();
        }

        if (!string.IsNullOrWhiteSpace(resourceEvent.TraceId))
        {
            attributes["traceId"] = resourceEvent.TraceId;
        }

        if (!string.IsNullOrWhiteSpace(resourceEvent.SpanId))
        {
            attributes["spanId"] = resourceEvent.SpanId;
        }

        return attributes;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? ResolveRecipientKey(ResourceEvent resourceEvent)
    {
        var triggeredBy = NormalizeOptional(resourceEvent.TriggeredBy);
        if (triggeredBy is null)
        {
            return null;
        }

        if (!IsSystemRecoveryProducer(triggeredBy))
        {
            return triggeredBy;
        }

        return IsRecoveryNotificationEvent(resourceEvent.EventType)
            ? LocalSystemNotificationRecipientKey
            : null;
    }

    private static bool IsSystemRecoveryProducer(string triggeredBy) =>
        triggeredBy.Equals("liveness", StringComparison.OrdinalIgnoreCase) ||
        triggeredBy.Equals("recovery", StringComparison.OrdinalIgnoreCase) ||
        triggeredBy.Equals("replica-management", StringComparison.OrdinalIgnoreCase);

    private static string CreateOperationCorrelationId(
        ResourceEvent resourceEvent,
        string operationKind,
        string operationId,
        string recipientKey) =>
        string.Join(
            "|",
            $"resource-{operationKind}",
            resourceEvent.ResourceId,
            operationId,
            recipientKey);

    private static NotificationOperation? GetOperation(ResourceEvent resourceEvent)
    {
        var lifecycleActionId = GetLifecycleActionId(resourceEvent.EventType);
        if (lifecycleActionId is not null)
        {
            return new NotificationOperation(
                "lifecycle",
                lifecycleActionId,
                "cloudshell.resource-lifecycle-operation");
        }

        if (IsStartMaterializationDeploymentEvent(resourceEvent))
        {
            return new NotificationOperation(
                "lifecycle",
                ResourceActionIds.Start,
                "cloudshell.resource-lifecycle-operation");
        }

        if (IsResourceCreateEvent(resourceEvent.EventType))
        {
            return new NotificationOperation(
                "create",
                "create",
                "cloudshell.resource-create-operation");
        }

        if (GetDeploymentUpdateKind(resourceEvent.EventType) is { } updateKind)
        {
            return new NotificationOperation(
                "update",
                updateKind,
                "cloudshell.resource-update-operation");
        }

        if (IsDeploymentApplyEvent(resourceEvent.EventType))
        {
            return new NotificationOperation(
                "deployment",
                "apply",
                "cloudshell.deployment-apply-operation");
        }

        if (IsResourceRecoveryNotificationEvent(resourceEvent.EventType))
        {
            return new NotificationOperation(
                "recovery",
                "resource",
                "cloudshell.resource-recovery-operation");
        }

        return IsReplicaRepairNotificationEvent(resourceEvent.EventType)
            ? new NotificationOperation(
                "replica-repair",
                "replica",
                "cloudshell.replica-repair-operation")
            : null;
    }

    private static string? GetLifecycleActionId(string eventType) =>
        eventType.Trim() switch
        {
            ResourceEventTypes.Events.Lifecycle.Starting or
            ResourceEventTypes.Events.Lifecycle.Started or
            ResourceEventTypes.Events.Lifecycle.StartFailed => ResourceActionIds.Start,
            ResourceEventTypes.Events.Lifecycle.Stopping or
            ResourceEventTypes.Events.Lifecycle.Stopped or
            ResourceEventTypes.Events.Lifecycle.StopFailed => ResourceActionIds.Stop,
            ResourceEventTypes.Events.Lifecycle.Pausing or
            ResourceEventTypes.Events.Lifecycle.Paused or
            ResourceEventTypes.Events.Lifecycle.PauseFailed => ResourceActionIds.Pause,
            ResourceEventTypes.Events.Lifecycle.Restarting or
            ResourceEventTypes.Events.Lifecycle.Restarted or
            ResourceEventTypes.Events.Lifecycle.RestartFailed => ResourceActionIds.Restart,
            _ => null
        };

    private static string GetLifecycleActionLabel(string actionId) =>
        actionId switch
        {
            ResourceActionIds.Start => "Start",
            ResourceActionIds.Stop => "Stop",
            ResourceActionIds.Pause => "Pause",
            ResourceActionIds.Restart => "Restart",
            _ => "Update"
        };

    private static bool IsResourceCreateEvent(string eventType) =>
        eventType.Trim() is
            ResourceEventTypes.Events.Resource.Creating or
            ResourceEventTypes.Events.Resource.Created or
            ResourceEventTypes.Events.Resource.CreateFailed;

    private static string? GetDeploymentUpdateKind(string eventType) =>
        eventType.Trim() switch
        {
            ResourceEventTypes.Events.Deployment.ImageUpdating or
            ResourceEventTypes.Events.Deployment.ImageUpdated or
            ResourceEventTypes.Events.Deployment.ImageUpdateFailed => "image",
            ResourceEventTypes.Events.Deployment.ReplicasUpdating or
            ResourceEventTypes.Events.Deployment.ReplicasUpdated or
            ResourceEventTypes.Events.Deployment.ReplicasUpdateFailed => "replicas",
            _ => null
        };

    private static bool IsDeploymentApplyEvent(string eventType) =>
        eventType.Trim() is
            ResourceEventTypes.Events.Deployment.Applying or
            ResourceEventTypes.Events.Deployment.Applied or
            ResourceEventTypes.Events.Deployment.Failed;

    private static bool IsStartMaterializationDeploymentEvent(ResourceEvent resourceEvent) =>
        IsDeploymentMaterializationProgressEvent(resourceEvent.EventType) &&
        resourceEvent.Message.Contains(ResourceStartMaterializationCause, StringComparison.OrdinalIgnoreCase);

    private static bool IsDeploymentMaterializationProgressEvent(string eventType) =>
        eventType.Trim() is
            ResourceEventTypes.Events.Deployment.Applying or
            ResourceEventTypes.Events.Deployment.ServiceReconciling or
            ResourceEventTypes.Events.Deployment.ServiceReconciled or
            ResourceEventTypes.Events.Deployment.ReplicaMaterializing or
            ResourceEventTypes.Events.Deployment.ReplicaMaterialized or
            ResourceEventTypes.Events.Deployment.RoutingUpdating or
            ResourceEventTypes.Events.Deployment.RoutingUpdated or
            ResourceEventTypes.Events.Deployment.Applied or
            ResourceEventTypes.Events.Deployment.Failed;

    private static bool IsRecoveryNotificationEvent(string eventType) =>
        IsResourceRecoveryNotificationEvent(eventType) ||
        IsReplicaRepairNotificationEvent(eventType);

    private static bool IsResourceRecoveryNotificationEvent(string eventType) =>
        eventType.Trim() is
            ResourceEventTypes.Events.Lifecycle.Degraded or
            ResourceEventTypes.Events.Lifecycle.StoppedUnexpectedly or
            ResourceEventTypes.Events.Recovery.RestartScheduled or
            ResourceEventTypes.Events.Recovery.RestartAttempted or
            ResourceEventTypes.Events.Recovery.RestartSucceeded or
            ResourceEventTypes.Events.Recovery.RestartFailed or
            ResourceEventTypes.Events.Recovery.RestartSkipped or
            ResourceEventTypes.Events.Recovery.RestartExhausted;

    private static bool IsReplicaRepairNotificationEvent(string eventType) =>
        eventType.Trim() is
            ResourceEventTypes.Events.ReplicaManagement.OccupantCrashed or
            ResourceEventTypes.Events.ReplicaManagement.RestartScheduled or
            ResourceEventTypes.Events.ReplicaManagement.RestartAttempted or
            ResourceEventTypes.Events.ReplicaManagement.RestartSucceeded or
            ResourceEventTypes.Events.ReplicaManagement.ReplacementScheduled or
            ResourceEventTypes.Events.ReplicaManagement.ReplacementMaterializing or
            ResourceEventTypes.Events.ReplicaManagement.ReplacementMaterialized or
            ResourceEventTypes.Events.ReplicaManagement.SlotLeftVacant or
            ResourceEventTypes.Events.ReplicaManagement.ReconciliationFailed or
            ResourceEventTypes.Events.ReplicaManagement.ReconciliationExhausted;

    private sealed record NotificationOperation(
        string Kind,
        string Id,
        string TemplateKey);
}
