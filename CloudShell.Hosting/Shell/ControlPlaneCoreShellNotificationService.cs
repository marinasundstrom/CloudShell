using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.Notifications;
using CloudShell.Abstractions.ResourceManager;
using CoreShell;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace CloudShell.Hosting.Shell;

internal sealed class ControlPlaneCoreShellNotificationService : ICoreShellNotificationService, IDisposable
{
    private const string PreferredUsernameClaimType = "preferred_username";
    private const string UnauthenticatedRequestActor = "user";
    private readonly ICloudShellNotificationManager? notifications;
    private readonly AuthenticationStateProvider authenticationStateProvider;

    public ControlPlaneCoreShellNotificationService(
        IEnumerable<ICloudShellNotificationManager> notificationManagers,
        AuthenticationStateProvider authenticationStateProvider)
    {
        ArgumentNullException.ThrowIfNull(notificationManagers);
        ArgumentNullException.ThrowIfNull(authenticationStateProvider);

        notifications = notificationManagers.FirstOrDefault();
        this.authenticationStateProvider = authenticationStateProvider;
        if (notifications is not null)
        {
            notifications.NotificationsChanged += OnNotificationsChanged;
        }
    }

    public event EventHandler<CoreShellNotificationsChangedEventArgs>? NotificationsChanged;

    public async Task<IReadOnlyList<CoreShellNotificationInstance>> GetNotificationsAsync(
        CoreShellNotificationQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        if (notifications is null)
        {
            return [];
        }

        query ??= new CoreShellNotificationQuery();
        var recipientKey = await GetRecipientKeyAsync();
        var notificationQuery = new CloudShellNotificationQuery(
            RecipientKey: recipientKey,
            IncludeDismissed: query.IncludeDismissed,
            MaxNotifications: query.Limit ?? 200);

        return (await notifications.ListNotificationsAsync(notificationQuery, cancellationToken))
            .Select(ToCoreShellNotification)
            .ToArray();
    }

    public Task AcknowledgeAsync(
        string notificationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(notificationId);
        return notifications?.AcknowledgeNotificationAsync(notificationId, cancellationToken) ?? Task.CompletedTask;
    }

    public Task HandleActionAsync(
        string notificationId,
        string actionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(notificationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);

        return notifications?.HandleNotificationActionAsync(notificationId, actionId, cancellationToken)
            ?? Task.CompletedTask;
    }

    public Task DismissAsync(
        string notificationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(notificationId);
        return notifications?.DismissNotificationAsync(notificationId, cancellationToken) ?? Task.CompletedTask;
    }

    public void Dispose()
    {
        if (notifications is not null)
        {
            notifications.NotificationsChanged -= OnNotificationsChanged;
        }
    }

    private void OnNotificationsChanged(object? sender, CloudShellNotificationsChangedEventArgs args) =>
        NotificationsChanged?.Invoke(
            this,
            new CoreShellNotificationsChangedEventArgs(ToCoreShellChangeKind(args.Kind), args.NotificationId));

    private async Task<string> GetRecipientKeyAsync()
    {
        var user = (await authenticationStateProvider.GetAuthenticationStateAsync()).User;
        if (user.Identity?.IsAuthenticated == true)
        {
            var value =
                FindActorClaim(user, PreferredUsernameClaimType) ??
                FindActorClaim(user, ClaimTypes.Upn) ??
                FindActorClaim(user, ClaimTypes.Email) ??
                FindActorClaim(user, ClaimTypes.Name) ??
                FindActorClaim(user, ClaimTypes.NameIdentifier);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return UnauthenticatedRequestActor;
    }

    private static string? FindActorClaim(ClaimsPrincipal user, string claimType) =>
        user.Claims
            .Where(claim => string.Equals(claim.Type, claimType, StringComparison.Ordinal))
            .Select(claim => claim.Value?.Trim())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static CoreShellNotificationInstance ToCoreShellNotification(
        CloudShellNotificationInstance notification) =>
        new(
            notification.Id,
            notification.Title,
            notification.Message,
            ToCoreShellSeverity(notification.Severity),
            ToCoreShellStatus(notification.Status),
            notification.CreatedAt,
            notification.UpdatedAt,
            Source: notification.Source,
            Target: CreateTarget(notification),
            EventId: notification.EventId,
            ReadAt: notification.ReadAt,
            AcknowledgedAt: notification.AcknowledgedAt,
            DismissedAt: notification.DismissedAt,
            Attributes: notification.Attributes,
            Actions: notification.Actions?
                .Select(ToCoreShellAction)
                .ToArray(),
            ToastBehavior: CoreShellNotificationToastBehavior.Default,
            ToastTimeToLive: CoreShellToastDefaults.NotificationTimeToLive,
            ToastAutoDismiss: CoreShellToastAutoDismissBehavior.AfterTimeToLive);

    private static CoreShellNotificationTarget? CreateTarget(CloudShellNotificationInstance notification) =>
        string.IsNullOrWhiteSpace(notification.ResourceId)
            ? null
            : new CoreShellNotificationTarget(
                $"/resources/{Uri.EscapeDataString(notification.ResourceId.Trim())}",
                "Open resource");

    private static CoreShellNotificationAction ToCoreShellAction(
        CloudShellNotificationAction action) =>
        new(
            action.Id,
            action.Label,
            action.Target is null
                ? null
                : new CoreShellNotificationTarget(action.Target.Href, action.Target.Label),
            action.IsPrimary);

    private static CoreShellNotificationSeverity ToCoreShellSeverity(ResourceSignalSeverity severity) =>
        severity switch
        {
            ResourceSignalSeverity.Success => CoreShellNotificationSeverity.Success,
            ResourceSignalSeverity.Warning => CoreShellNotificationSeverity.Warning,
            ResourceSignalSeverity.Error => CoreShellNotificationSeverity.Error,
            _ => CoreShellNotificationSeverity.Info
        };

    private static CoreShellNotificationStatus ToCoreShellStatus(CloudShellNotificationStatus status) =>
        status switch
        {
            CloudShellNotificationStatus.InProgress => CoreShellNotificationStatus.InProgress,
            CloudShellNotificationStatus.Succeeded => CoreShellNotificationStatus.Succeeded,
            CloudShellNotificationStatus.Failed => CoreShellNotificationStatus.Failed,
            CloudShellNotificationStatus.NeedsAttention => CoreShellNotificationStatus.NeedsAttention,
            _ => CoreShellNotificationStatus.Active
        };

    private static CoreShellNotificationChangeKind ToCoreShellChangeKind(CloudShellNotificationChangeKind kind) =>
        kind switch
        {
            CloudShellNotificationChangeKind.Created => CoreShellNotificationChangeKind.Created,
            CloudShellNotificationChangeKind.Updated => CoreShellNotificationChangeKind.Updated,
            CloudShellNotificationChangeKind.Acknowledged => CoreShellNotificationChangeKind.Acknowledged,
            CloudShellNotificationChangeKind.Dismissed => CoreShellNotificationChangeKind.Dismissed,
            _ => CoreShellNotificationChangeKind.RefreshRequired
        };
}
