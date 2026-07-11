using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.Notifications;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Hosting.Shell;
using CoreShell;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace CloudShell.Abstractions.Tests;

public sealed class ControlPlaneCoreShellNotificationServiceTests
{
    [Fact]
    public async Task ControlPlaneAdapter_MapsNotificationBackedToastsToAutoDismissByTimeToLive()
    {
        var manager = new TestCloudShellNotificationManager(
            new CloudShellNotificationInstance(
                "notification-1",
                "user",
                "Resource started",
                "The resource started.",
                ResourceSignalSeverity.Success,
                CloudShellNotificationStatus.Succeeded,
                DateTimeOffset.UtcNow.AddSeconds(-1),
                DateTimeOffset.UtcNow,
                ResourceId: "application:api"));
        var service = new ControlPlaneCoreShellNotificationService(
            [manager],
            new StaticAuthenticationStateProvider());

        var notification = Assert.Single(await service.GetNotificationsAsync());

        Assert.Equal(CoreShellToastAutoDismissBehavior.AfterTimeToLive, notification.ToastAutoDismiss);
        Assert.Equal(CoreShellToastDefaults.NotificationTimeToLive, notification.ToastTimeToLive);
    }

    private sealed class StaticAuthenticationStateProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    private sealed class TestCloudShellNotificationManager(
        params CloudShellNotificationInstance[] notifications) : ICloudShellNotificationManager
    {
        public event EventHandler<CloudShellNotificationsChangedEventArgs>? NotificationsChanged
        {
            add { }
            remove { }
        }

        public Task<IReadOnlyList<CloudShellNotificationInstance>> ListNotificationsAsync(
            CloudShellNotificationQuery? query = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CloudShellNotificationInstance>>(
                notifications
                    .Where(notification =>
                        string.IsNullOrWhiteSpace(query?.RecipientKey) ||
                        string.Equals(notification.RecipientKey, query.RecipientKey, StringComparison.OrdinalIgnoreCase))
                    .ToArray());

        public Task<CloudShellNotificationInstance> CreateNotificationAsync(
            CreateCloudShellNotificationCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AcknowledgeNotificationAsync(
            string notificationId,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task HandleNotificationActionAsync(
            string notificationId,
            string actionId,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DismissNotificationAsync(
            string notificationId,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
