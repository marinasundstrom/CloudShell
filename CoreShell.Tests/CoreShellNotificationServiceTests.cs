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

    [Fact]
    public async Task EmptyToastService_ReturnsNoToastsAndValidatesInputs()
    {
        ICoreShellToastService service = new EmptyCoreShellToastService();

        var toast = await service.PublishAsync(new CoreShellToastRequest(
            "Saved",
            "The settings were saved."));

        Assert.NotEmpty(toast.Id);
        Assert.Equal("Saved", toast.Title);
        Assert.Empty(await service.GetToastsAsync());

        await service.HandleActionAsync("toast-1", "open");
        await service.DismissAsync("toast-1");

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.PublishAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => service.PublishAsync(new CoreShellToastRequest("", "Message")));
        await Assert.ThrowsAsync<ArgumentException>(() => service.PublishAsync(new CoreShellToastRequest("Title", " ")));
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateAsync("", new CoreShellToastUpdate()));
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.UpdateAsync("toast-1", null!));
        await Assert.ThrowsAsync<ArgumentException>(() => service.HandleActionAsync("", "open"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.HandleActionAsync("toast-1", ""));
        await Assert.ThrowsAsync<ArgumentException>(() => service.DismissAsync(" "));
    }

    [Fact]
    public void AddCoreShellBlazor_RegistersEmptyToastServiceByDefault()
    {
        var services = new ServiceCollection();

        services.AddCoreShellBlazor();

        var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<ICoreShellToastService>();
        Assert.IsType<EmptyCoreShellToastService>(service);
    }

    [Fact]
    public void AddCoreShellBlazor_PreservesHostToastService()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICoreShellToastService, TestToastService>();

        services.AddCoreShellBlazor();

        var provider = services.BuildServiceProvider();
        Assert.IsType<TestToastService>(provider.GetRequiredService<ICoreShellToastService>());
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

    private sealed class TestToastService : ICoreShellToastService
    {
        public event EventHandler<CoreShellToastsChangedEventArgs>? ToastsChanged;

        public Task<IReadOnlyList<CoreShellToast>> GetToastsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CoreShellToast>>([]);

        public Task<CoreShellToast> PublishAsync(
            CoreShellToastRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CoreShellToast(
                "toast-1",
                request.Title,
                request.Message,
                request.Severity,
                request.Status,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow));

        public Task<CoreShellToast?> UpdateAsync(
            string toastId,
            CoreShellToastUpdate update,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<CoreShellToast?>(null);

        public Task HandleActionAsync(
            string toastId,
            string actionId,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DismissAsync(
            string toastId,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public void RaiseChanged() =>
            ToastsChanged?.Invoke(this, new CoreShellToastsChangedEventArgs());
    }
}
