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
    Acknowledged = 1,
    Dismissed = 2
}

public sealed record CloudShellNotificationTarget(
    string Href,
    string? Label = null);

public sealed record CloudShellNotificationAction(
    string Id,
    string Label,
    CloudShellNotificationTarget? Target = null,
    bool IsPrimary = false);

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
