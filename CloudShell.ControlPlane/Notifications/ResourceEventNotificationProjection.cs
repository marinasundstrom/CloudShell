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

        var operation = GetOperation(resourceEvent.EventType);
        return new CreateCloudShellNotificationCommand(
            recipientKey,
            CreateTitle(resourceEvent),
            resourceEvent.Message,
            resourceEvent.Severity,
            CreateStatus(resourceEvent),
            Source: "Control Plane",
            ResourceId: resourceEvent.ResourceId,
            EventType: resourceEvent.EventType,
            EventId: CreateEventId(resourceEvent),
            CorrelationId: operation is null
                ? resourceEvent.TraceId
                : CreateOperationCorrelationId(resourceEvent, operation.Kind, operation.Id, recipientKey),
            TemplateKey: operation is null
                ? "cloudshell.resource-event"
                : operation.TemplateKey,
            Attributes: CreateAttributes(resourceEvent));
    }

    private static CloudShellNotificationStatus CreateStatus(ResourceEvent resourceEvent)
    {
        var operationStatus = CreateOperationStatus(resourceEvent.EventType);
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

    private static CloudShellNotificationStatus? CreateOperationStatus(string eventType) =>
        eventType.Trim() switch
        {
            ResourceEventTypes.Events.Recovery.RestartScheduled or
            ResourceEventTypes.Events.Recovery.RestartAttempted => CloudShellNotificationStatus.InProgress,
            ResourceEventTypes.Events.Recovery.RestartSucceeded => CloudShellNotificationStatus.Succeeded,
            ResourceEventTypes.Events.Recovery.RestartFailed or
            ResourceEventTypes.Events.Recovery.RestartExhausted => CloudShellNotificationStatus.Failed,
            ResourceEventTypes.Events.Lifecycle.Degraded or
            ResourceEventTypes.Events.Lifecycle.StoppedUnexpectedly or
            ResourceEventTypes.Events.Recovery.RestartSkipped => CloudShellNotificationStatus.NeedsAttention,
            _ => null
        };

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
        if (lifecycleActionId is not null)
        {
            return $"{GetLifecycleActionLabel(lifecycleActionId)} resource";
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

        if (IsRecoveryNotificationEvent(resourceEvent.EventType))
        {
            return "Resource recovery";
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

    private static IReadOnlyDictionary<string, string> CreateAttributes(ResourceEvent resourceEvent)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["resourceId"] = resourceEvent.ResourceId,
            ["eventType"] = resourceEvent.EventType
        };

        var lifecycleActionId = GetLifecycleActionId(resourceEvent.EventType);
        if (lifecycleActionId is not null)
        {
            attributes["actionId"] = lifecycleActionId;
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
        else if (IsRecoveryNotificationEvent(resourceEvent.EventType))
        {
            attributes["operationKind"] = "recovery";
            attributes["recoveryKind"] = "runtime";
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
        triggeredBy.Equals("recovery", StringComparison.OrdinalIgnoreCase);

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

    private static NotificationOperation? GetOperation(string eventType)
    {
        var lifecycleActionId = GetLifecycleActionId(eventType);
        if (lifecycleActionId is not null)
        {
            return new NotificationOperation(
                "lifecycle",
                lifecycleActionId,
                "cloudshell.resource-lifecycle-operation");
        }

        if (IsResourceCreateEvent(eventType))
        {
            return new NotificationOperation(
                "create",
                "create",
                "cloudshell.resource-create-operation");
        }

        if (GetDeploymentUpdateKind(eventType) is { } updateKind)
        {
            return new NotificationOperation(
                "update",
                updateKind,
                "cloudshell.resource-update-operation");
        }

        return IsRecoveryNotificationEvent(eventType)
            ? new NotificationOperation(
                "recovery",
                "runtime",
                "cloudshell.resource-recovery-operation")
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

    private static bool IsRecoveryNotificationEvent(string eventType) =>
        eventType.Trim() is
            ResourceEventTypes.Events.Lifecycle.Degraded or
            ResourceEventTypes.Events.Lifecycle.StoppedUnexpectedly or
            ResourceEventTypes.Events.Recovery.RestartScheduled or
            ResourceEventTypes.Events.Recovery.RestartAttempted or
            ResourceEventTypes.Events.Recovery.RestartSucceeded or
            ResourceEventTypes.Events.Recovery.RestartFailed or
            ResourceEventTypes.Events.Recovery.RestartSkipped or
            ResourceEventTypes.Events.Recovery.RestartExhausted;

    private sealed record NotificationOperation(
        string Kind,
        string Id,
        string TemplateKey);
}
