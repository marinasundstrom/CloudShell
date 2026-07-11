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
}
