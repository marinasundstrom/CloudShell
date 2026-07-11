namespace CoreShell;

public static class CoreShellNotificationPresentation
{
    public static IReadOnlyList<CoreShellNotificationInstance> SelectToastItems(
        IEnumerable<CoreShellNotificationInstance> notifications,
        DateTimeOffset now,
        int maxItems)
    {
        ArgumentNullException.ThrowIfNull(notifications);

        if (maxItems <= 0)
        {
            return [];
        }

        return notifications
            .Where(notification => ShouldShowToast(notification, now))
            .Take(maxItems)
            .ToArray();
    }

    public static IReadOnlyList<CoreShellToast> SelectToastItems(
        IEnumerable<CoreShellToast> toasts,
        DateTimeOffset now,
        int maxItems)
    {
        ArgumentNullException.ThrowIfNull(toasts);

        if (maxItems <= 0)
        {
            return [];
        }

        return toasts
            .Where(toast => ShouldShowToast(toast, now))
            .Take(maxItems)
            .ToArray();
    }

    public static bool ShouldShowToast(
        CoreShellNotificationInstance notification,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (notification.AcknowledgedAt is not null ||
            notification.DismissedAt is not null ||
            notification.ToastBehavior == CoreShellNotificationToastBehavior.Suppressed ||
            notification.VisibleAt > now)
        {
            return false;
        }

        if (notification.ToastBehavior == CoreShellNotificationToastBehavior.UntilAcknowledged ||
            notification.ToastAutoDismiss == CoreShellToastAutoDismissBehavior.Never)
        {
            return true;
        }

        if (notification.Status == CoreShellNotificationStatus.InProgress)
        {
            return true;
        }

        var timeToLive = notification.ToastTimeToLive ?? CoreShellToastDefaults.NotificationTimeToLive;
        return notification.UpdatedAt.Add(timeToLive) > now;
    }

    public static bool ShouldShowToast(
        CoreShellToast toast,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(toast);

        if (toast.AutoDismiss == CoreShellToastAutoDismissBehavior.Never)
        {
            return true;
        }

        if (toast.Status == CoreShellNotificationStatus.InProgress)
        {
            return true;
        }

        var timeToLive = toast.TimeToLive ?? CoreShellToastDefaults.DefaultTimeToLive;
        return toast.UpdatedAt.Add(timeToLive) > now;
    }
}
