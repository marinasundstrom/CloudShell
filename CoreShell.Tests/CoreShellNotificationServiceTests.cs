using CoreShell.Blazor;
using Microsoft.Extensions.DependencyInjection;

namespace CoreShell.Tests;

public sealed class CoreShellNotificationServiceTests
{
    [Fact]
    public async Task EmptyService_ReturnsNoNotifications()
    {
        ICoreShellNotificationService service = new EmptyCoreShellNotificationService();

        var notifications = await service.GetNotificationsAsync();

        Assert.Empty(notifications);
    }

    [Fact]
    public async Task EmptyService_ValidatesInstanceIds()
    {
        ICoreShellNotificationService service = new EmptyCoreShellNotificationService();

        await service.AcknowledgeAsync("notification-1");
        await service.HandleActionAsync("notification-1", "open");
        await service.DismissAsync("notification-1");

        await Assert.ThrowsAsync<ArgumentException>(() => service.AcknowledgeAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(() => service.HandleActionAsync("", "open"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.HandleActionAsync("notification-1", ""));
        await Assert.ThrowsAsync<ArgumentException>(() => service.DismissAsync(" "));
    }

    [Fact]
    public void AddCoreShellBlazor_RegistersEmptyNotificationServiceByDefault()
    {
        var services = new ServiceCollection();

        services.AddCoreShellBlazor();

        var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<ICoreShellNotificationService>();
        Assert.IsType<EmptyCoreShellNotificationService>(service);
    }

    [Fact]
    public void AddCoreShellBlazor_PreservesHostNotificationService()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICoreShellNotificationService, TestNotificationService>();

        services.AddCoreShellBlazor();

        var provider = services.BuildServiceProvider();
        Assert.IsType<TestNotificationService>(provider.GetRequiredService<ICoreShellNotificationService>());
    }

    private sealed class TestNotificationService : ICoreShellNotificationService
    {
        public event EventHandler<CoreShellNotificationsChangedEventArgs>? NotificationsChanged;

        public Task<IReadOnlyList<CoreShellNotificationInstance>> GetNotificationsAsync(
            CoreShellNotificationQuery? query = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CoreShellNotificationInstance>>([]);

        public Task AcknowledgeAsync(
            string notificationId,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task HandleActionAsync(
            string notificationId,
            string actionId,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DismissAsync(
            string notificationId,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public void RaiseChanged() =>
            NotificationsChanged?.Invoke(this, new CoreShellNotificationsChangedEventArgs());
    }
}
