using CoreShell;

namespace CoreShell.FluentUiSample;

public sealed class SampleNotificationService :
    ICoreShellNotificationService,
    ICoreShellNotificationProducer
{
    private readonly object _gate = new();
    private readonly List<CoreShellNotificationInstance> _notifications = [];

    public event EventHandler<CoreShellNotificationsChangedEventArgs>? NotificationsChanged;

    public Task<IReadOnlyList<CoreShellNotificationInstance>> GetNotificationsAsync(
        CoreShellNotificationQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        query ??= new CoreShellNotificationQuery();

        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            IEnumerable<CoreShellNotificationInstance> notifications = _notifications;
            if (!query.IncludeDismissed)
            {
                notifications = notifications.Where(notification => notification.DismissedAt is null);
            }

            if (!query.IncludeScheduled)
            {
                notifications = notifications.Where(notification =>
                    notification.VisibleAt is null || notification.VisibleAt <= now);
            }

            notifications = notifications
                .OrderByDescending(notification => notification.UpdatedAt)
                .ThenByDescending(notification => notification.CreatedAt);

            if (query.Limit is { } limit and > 0)
            {
                notifications = notifications.Take(limit);
            }

            return Task.FromResult<IReadOnlyList<CoreShellNotificationInstance>>(notifications.ToArray());
        }
    }

    public Task<CoreShellNotificationInstance> PublishAsync(
        CoreShellNotificationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = DateTimeOffset.UtcNow;
        var autoDismiss = NormalizeAutoDismiss(request.ToastAutoDismiss);
        var notification = new CoreShellNotificationInstance(
            Guid.NewGuid().ToString("n"),
            NormalizeRequired(request.Title, nameof(request.Title)),
            NormalizeRequired(request.Message, nameof(request.Message)),
            request.Severity,
            request.Status,
            now,
            now,
            Source: NormalizeOptional(request.Source),
            Target: request.Target,
            EventId: NormalizeOptional(request.EventId),
            Attributes: request.Attributes,
            Actions: NormalizeActions(request.Actions),
            ToastBehavior: request.ToastBehavior,
            ToastTimeToLive: autoDismiss == CoreShellToastAutoDismissBehavior.Never
                ? null
                : request.ToastTimeToLive,
            ToastAutoDismiss: autoDismiss,
            VisibleAt: ResolveVisibleAt(now, request.VisibleAt, request.VisibleIn));

        lock (_gate)
        {
            _notifications.Add(notification);
        }

        RaiseChanged(CoreShellNotificationChangeKind.Created, notification.Id);
        return Task.FromResult(notification);
    }

    public Task<CoreShellNotificationInstance?> UpdateAsync(
        string notificationId,
        CoreShellNotificationUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(notificationId);
        ArgumentNullException.ThrowIfNull(update);

        CoreShellNotificationInstance? notification = null;
        lock (_gate)
        {
            var index = _notifications.FindIndex(item =>
                string.Equals(item.Id, notificationId, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return Task.FromResult<CoreShellNotificationInstance?>(null);
            }

            notification = _notifications[index] with
            {
                Title = NormalizeOptional(update.Title) ?? _notifications[index].Title,
                Message = NormalizeOptional(update.Message) ?? _notifications[index].Message,
                Severity = update.Severity ?? _notifications[index].Severity,
                Status = update.Status ?? _notifications[index].Status,
                Target = update.Target ?? _notifications[index].Target,
                Attributes = update.Attributes ?? _notifications[index].Attributes,
                Actions = update.Actions is null
                    ? _notifications[index].Actions
                    : NormalizeActions(update.Actions),
                ToastTimeToLive = update.ToastTimeToLive ?? _notifications[index].ToastTimeToLive,
                ToastAutoDismiss = update.ToastAutoDismiss.HasValue
                    ? NormalizeAutoDismiss(update.ToastAutoDismiss.Value)
                    : _notifications[index].ToastAutoDismiss,
                VisibleAt = update.VisibleAt ?? _notifications[index].VisibleAt,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _notifications[index] = notification;
        }

        RaiseChanged(CoreShellNotificationChangeKind.Updated, notification.Id);
        return Task.FromResult<CoreShellNotificationInstance?>(notification);
    }

    public Task AcknowledgeAsync(
        string notificationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(notificationId);

        var changed = UpdateInstance(
            notificationId,
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

        if (changed)
        {
            RaiseChanged(CoreShellNotificationChangeKind.Acknowledged, notificationId);
        }

        return Task.CompletedTask;
    }

    public Task DismissAsync(
        string notificationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(notificationId);

        var changed = UpdateInstance(
            notificationId,
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

        if (changed)
        {
            RaiseChanged(CoreShellNotificationChangeKind.Dismissed, notificationId);
        }

        return Task.CompletedTask;
    }

    public Task HandleActionAsync(
        string notificationId,
        string actionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(notificationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);

        var changed = false;
        lock (_gate)
        {
            var index = _notifications.FindIndex(item =>
                string.Equals(item.Id, notificationId, StringComparison.OrdinalIgnoreCase));
            if (index >= 0
                && _notifications[index].Actions?.Any(item =>
                    string.Equals(item.Id, actionId, StringComparison.OrdinalIgnoreCase)) == true)
            {
                var now = DateTimeOffset.UtcNow;
                _notifications[index] = _notifications[index] with
                {
                    AcknowledgedAt = _notifications[index].AcknowledgedAt ?? now,
                    ReadAt = _notifications[index].ReadAt ?? now,
                    UpdatedAt = now
                };
                changed = true;
            }
        }

        if (changed)
        {
            RaiseChanged(CoreShellNotificationChangeKind.Acknowledged, notificationId);
        }

        return Task.CompletedTask;
    }

    private bool UpdateInstance(
        string notificationId,
        Func<CoreShellNotificationInstance, CoreShellNotificationInstance> update)
    {
        lock (_gate)
        {
            var index = _notifications.FindIndex(item =>
                string.Equals(item.Id, notificationId, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return false;
            }

            _notifications[index] = update(_notifications[index]);
            return true;
        }
    }

    private void RaiseChanged(CoreShellNotificationChangeKind kind, string notificationId) =>
        NotificationsChanged?.Invoke(this, new CoreShellNotificationsChangedEventArgs(kind, notificationId));

    private static string NormalizeRequired(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static CoreShellToastAutoDismissBehavior NormalizeAutoDismiss(
        CoreShellToastAutoDismissBehavior autoDismiss) =>
        autoDismiss == CoreShellToastAutoDismissBehavior.Default
            ? CoreShellToastAutoDismissBehavior.AfterTimeToLive
            : autoDismiss;

    private static DateTimeOffset? ResolveVisibleAt(
        DateTimeOffset now,
        DateTimeOffset? visibleAt,
        TimeSpan? visibleIn)
    {
        if (visibleAt.HasValue && visibleIn.HasValue)
        {
            throw new ArgumentException("Specify either VisibleAt or VisibleIn, not both.");
        }

        if (visibleIn is { } delay)
        {
            if (delay < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(visibleIn), delay, "VisibleIn cannot be negative.");
            }

            return now.Add(delay);
        }

        return visibleAt;
    }

    private static IReadOnlyList<CoreShellNotificationAction>? NormalizeActions(
        IReadOnlyList<CoreShellNotificationAction>? actions)
    {
        if (actions is null || actions.Count == 0)
        {
            return null;
        }

        return actions
            .Select(action => action with
            {
                Id = NormalizeRequired(action.Id, nameof(action.Id)),
                Label = NormalizeRequired(action.Label, nameof(action.Label))
            })
            .ToArray();
    }
}
