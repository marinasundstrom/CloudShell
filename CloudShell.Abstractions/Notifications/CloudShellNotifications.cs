using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Abstractions.Notifications;

public enum CloudShellNotificationStatus
{
    Active = 0,
    InProgress = 1,
    Succeeded = 2,
    Failed = 3,
    NeedsAttention = 4
}

public enum CloudShellNotificationChangeKind
{
    Created = 0,
    Updated = 1,
    Acknowledged = 2,
    Dismissed = 3
}

public sealed record CloudShellNotificationTarget(
    string Href,
    string? Label = null);

public sealed record CloudShellNotificationAction(
    string Id,
    string Label,
    CloudShellNotificationTarget? Target = null,
    bool IsPrimary = false);

public static class CloudShellNotificationActionIds
{
    public const string OpenResource = "open-resource";
    public const string ViewActivity = "view-activity";
}

public static class CloudShellNotificationTemplateKeys
{
    public const string ResourceLifecycleOperation = "cloudshell.resource-lifecycle-operation";
    public const string ResourceCreateOperation = "cloudshell.resource-create-operation";
    public const string ResourceUpdateOperation = "cloudshell.resource-update-operation";
    public const string DeploymentApplyOperation = "cloudshell.deployment-apply-operation";
    public const string ResourceRecoveryOperation = "cloudshell.resource-recovery-operation";
    public const string ReplicaRepairOperation = "cloudshell.replica-repair-operation";
    public const string ResourceTemplateApplyOperation = "cloudshell.resource-template-apply-operation";
    public const string ApplicationArtifactApplyOperation = "cloudshell.application-artifact-apply-operation";
}

public sealed record CloudShellNotificationQuery(
    string? RecipientKey = null,
    bool IncludeDismissed = false,
    int MaxNotifications = 200);

public sealed record CreateCloudShellNotificationCommand(
    string RecipientKey,
    string Title,
    string Message,
    ResourceSignalSeverity Severity,
    CloudShellNotificationStatus Status,
    string? Source = null,
    string? ResourceId = null,
    string? EventType = null,
    string? EventId = null,
    string? CorrelationId = null,
    string? TemplateKey = null,
    IReadOnlyList<CloudShellNotificationAction>? Actions = null,
    IReadOnlyDictionary<string, string>? Attributes = null);

public sealed record CloudShellNotificationInstance(
    string Id,
    string RecipientKey,
    string Title,
    string Message,
    ResourceSignalSeverity Severity,
    CloudShellNotificationStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? Source = null,
    string? ResourceId = null,
    string? EventType = null,
    string? EventId = null,
    string? CorrelationId = null,
    string? TemplateKey = null,
    DateTimeOffset? ReadAt = null,
    DateTimeOffset? AcknowledgedAt = null,
    DateTimeOffset? DismissedAt = null,
    IReadOnlyList<CloudShellNotificationAction>? Actions = null,
    IReadOnlyDictionary<string, string>? Attributes = null);

public sealed class CloudShellNotificationsChangedEventArgs(
    CloudShellNotificationChangeKind kind,
    string notificationId) : EventArgs
{
    public CloudShellNotificationChangeKind Kind { get; } = kind;

    public string NotificationId { get; } = notificationId;
}

public interface ICloudShellNotificationStore
{
    event EventHandler<CloudShellNotificationsChangedEventArgs>? NotificationsChanged;

    IReadOnlyList<CloudShellNotificationInstance> GetNotifications(
        CloudShellNotificationQuery? query = null);

    CloudShellNotificationInstance CreateNotification(
        CreateCloudShellNotificationCommand command);

    CloudShellNotificationInstance CreateOrUpdateNotification(
        CreateCloudShellNotificationCommand command);

    CloudShellNotificationInstance? GetNotification(string notificationId);

    bool AcknowledgeNotification(string notificationId);

    bool DismissNotification(string notificationId);
}

public interface ICloudShellNotificationActionHandler
{
    Task HandleNotificationActionAsync(
        CloudShellNotificationInstance notification,
        string actionId,
        CancellationToken cancellationToken = default);
}

public interface IResourceEventNotificationRule
{
    CreateCloudShellNotificationCommand? CreateNotification(ResourceEvent resourceEvent);
}
