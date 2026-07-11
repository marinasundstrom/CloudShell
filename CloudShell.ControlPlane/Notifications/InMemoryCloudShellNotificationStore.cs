using CloudShell.Abstractions.Notifications;
using System.Collections.Concurrent;

namespace CloudShell.ControlPlane.Notifications;

public sealed class InMemoryCloudShellNotificationStore : ICloudShellNotificationStore
{
    private const int MaxNotifications = 1_000;
    private readonly ConcurrentDictionary<string, CloudShellNotificationInstance> notifications =
        new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<CloudShellNotificationsChangedEventArgs>? NotificationsChanged;

    public IReadOnlyList<CloudShellNotificationInstance> GetNotifications(
        CloudShellNotificationQuery? query = null)
    {
        query ??= new CloudShellNotificationQuery();
        var maxNotifications = Math.Clamp(query.MaxNotifications, 1, MaxNotifications);

        IEnumerable<CloudShellNotificationInstance> matches = notifications.Values;
        if (!string.IsNullOrWhiteSpace(query.RecipientKey))
        {
            matches = matches.Where(notification =>
                string.Equals(notification.RecipientKey, query.RecipientKey, StringComparison.OrdinalIgnoreCase));
        }

        if (!query.IncludeDismissed)
        {
            matches = matches.Where(notification => notification.DismissedAt is null);
        }

        return matches
            .OrderByDescending(notification => notification.UpdatedAt)
            .ThenByDescending(notification => notification.CreatedAt)
            .Take(maxNotifications)
            .ToArray();
    }

    public CloudShellNotificationInstance CreateNotification(
        CreateCloudShellNotificationCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = DateTimeOffset.UtcNow;
        var notification = new CloudShellNotificationInstance(
            Guid.NewGuid().ToString("n"),
            NormalizeRequired(command.RecipientKey, nameof(command.RecipientKey)),
            NormalizeRequired(command.Title, nameof(command.Title)),
            NormalizeRequired(command.Message, nameof(command.Message)),
            command.Severity,
            command.Status,
            now,
            now,
            Source: NormalizeOptional(command.Source),
            ResourceId: NormalizeOptional(command.ResourceId),
            EventType: NormalizeOptional(command.EventType),
            EventId: NormalizeOptional(command.EventId),
            CorrelationId: NormalizeOptional(command.CorrelationId),
            TemplateKey: NormalizeOptional(command.TemplateKey),
            Actions: NormalizeActions(command.Actions),
            Attributes: NormalizeAttributes(command.Attributes));

        notifications[notification.Id] = notification;
        NotificationsChanged?.Invoke(
            this,
            new CloudShellNotificationsChangedEventArgs(
                CloudShellNotificationChangeKind.Created,
                notification.Id));

        return notification;
    }

    public CloudShellNotificationInstance? GetNotification(string notificationId)
    {
        notificationId = NormalizeRequired(notificationId, nameof(notificationId));
        return notifications.GetValueOrDefault(notificationId);
    }

    public bool AcknowledgeNotification(string notificationId) =>
        UpdateNotification(
            notificationId,
            CloudShellNotificationChangeKind.Acknowledged,
            notification =>
            {
                var now = DateTimeOffset.UtcNow;
                return notification with
                {
                    AcknowledgedAt = notification.AcknowledgedAt ?? now,
                    ReadAt = notification.ReadAt ?? now,
                    UpdatedAt = now
                };
            });

    public bool DismissNotification(string notificationId) =>
        UpdateNotification(
            notificationId,
            CloudShellNotificationChangeKind.Dismissed,
            notification =>
            {
                var now = DateTimeOffset.UtcNow;
                return notification with
                {
                    DismissedAt = notification.DismissedAt ?? now,
                    ReadAt = notification.ReadAt ?? now,
                    UpdatedAt = now
                };
            });

    private bool UpdateNotification(
        string notificationId,
        CloudShellNotificationChangeKind changeKind,
        Func<CloudShellNotificationInstance, CloudShellNotificationInstance> update)
    {
        notificationId = NormalizeRequired(notificationId, nameof(notificationId));

        while (notifications.TryGetValue(notificationId, out var existing))
        {
            var updated = update(existing);
            if (notifications.TryUpdate(notificationId, updated, existing))
            {
                NotificationsChanged?.Invoke(
                    this,
                    new CloudShellNotificationsChangedEventArgs(changeKind, updated.Id));
                return true;
            }
        }

        return false;
    }

    private static string NormalizeRequired(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyDictionary<string, string>? NormalizeAttributes(
        IReadOnlyDictionary<string, string>? attributes)
    {
        if (attributes is null || attributes.Count == 0)
        {
            return null;
        }

        return attributes
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(
                pair => pair.Key.Trim(),
                pair => pair.Value.Trim(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<CloudShellNotificationAction>? NormalizeActions(
        IReadOnlyList<CloudShellNotificationAction>? actions)
    {
        if (actions is null || actions.Count == 0)
        {
            return null;
        }

        return actions
            .Where(action => !string.IsNullOrWhiteSpace(action.Id) && !string.IsNullOrWhiteSpace(action.Label))
            .Select(action => action with
            {
                Id = action.Id.Trim(),
                Label = action.Label.Trim(),
                Target = NormalizeTarget(action.Target)
            })
            .ToArray();
    }

    private static CloudShellNotificationTarget? NormalizeTarget(CloudShellNotificationTarget? target) =>
        target is null || string.IsNullOrWhiteSpace(target.Href)
            ? null
            : target with
            {
                Href = target.Href.Trim(),
                Label = NormalizeOptional(target.Label)
            };
}
