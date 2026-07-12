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
    public async Task EmptyNotificationProducer_ReturnsCreatedReferenceAndValidatesInputs()
    {
        ICoreShellNotificationProducer producer = new EmptyCoreShellNotificationProducer();
        var visibleAt = DateTimeOffset.UtcNow.AddMinutes(5);

        var notification = await producer.PublishAsync(new CoreShellNotificationRequest(
            "Creating resource",
            "The operation is running.",
            CoreShellNotificationSeverity.Info,
            CoreShellNotificationStatus.InProgress,
            Source: "Test",
            VisibleAt: visibleAt));

        Assert.NotEmpty(notification.Id);
        Assert.Equal("Creating resource", notification.Title);
        Assert.Equal(CoreShellToastAutoDismissBehavior.AfterTimeToLive, notification.ToastAutoDismiss);
        Assert.Equal(CoreShellToastDefaults.NotificationTimeToLive, notification.ToastTimeToLive);
        Assert.Equal(visibleAt, notification.VisibleAt);
        Assert.Null(await producer.UpdateAsync(notification.Id, new CoreShellNotificationUpdate()));
        await producer.DismissAsync(notification.Id);

        await Assert.ThrowsAsync<ArgumentNullException>(() => producer.PublishAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => producer.PublishAsync(new CoreShellNotificationRequest("", "Message")));
        await Assert.ThrowsAsync<ArgumentException>(() => producer.PublishAsync(new CoreShellNotificationRequest("Title", " ")));
        await Assert.ThrowsAsync<ArgumentException>(() => producer.PublishAsync(new CoreShellNotificationRequest(
            "Title",
            "Message",
            VisibleAt: visibleAt,
            VisibleIn: TimeSpan.FromSeconds(1))));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => producer.PublishAsync(new CoreShellNotificationRequest(
            "Title",
            "Message",
            VisibleIn: TimeSpan.FromSeconds(-1))));
        await Assert.ThrowsAsync<ArgumentException>(() => producer.UpdateAsync("", new CoreShellNotificationUpdate()));
        await Assert.ThrowsAsync<ArgumentNullException>(() => producer.UpdateAsync(notification.Id, null!));
        await Assert.ThrowsAsync<ArgumentException>(() => producer.DismissAsync(" "));
    }

    [Fact]
    public async Task EmptyNotificationProducer_CanReturnNeverAutoDismissReference()
    {
        ICoreShellNotificationProducer producer = new EmptyCoreShellNotificationProducer();

        var notification = await producer.PublishAsync(new CoreShellNotificationRequest(
            "Creating resource",
            "The operation is running.",
            CoreShellNotificationSeverity.Info,
            CoreShellNotificationStatus.InProgress,
            ToastAutoDismiss: CoreShellToastAutoDismissBehavior.Never));

        Assert.Equal(CoreShellToastAutoDismissBehavior.Never, notification.ToastAutoDismiss);
        Assert.Null(notification.ToastTimeToLive);
    }

    [Fact]
    public void NotificationPresentation_ExpiresPlainToastsAfterTimeToLive()
    {
        var now = DateTimeOffset.UtcNow;
        var notification = CreateNotification(
            updatedAt: now.Subtract(TimeSpan.FromSeconds(9)),
            toastTimeToLive: TimeSpan.FromSeconds(8));

        Assert.False(CoreShellNotificationPresentation.ShouldShowToast(notification, now));
    }

    [Fact]
    public void NotificationPresentation_KeepsTerminalToastsDuringTimeToLiveAfterUpdate()
    {
        var now = DateTimeOffset.UtcNow;
        var notification = CreateNotification(
            status: CoreShellNotificationStatus.Succeeded,
            updatedAt: now.Subtract(TimeSpan.FromSeconds(2)),
            toastTimeToLive: TimeSpan.FromSeconds(8));

        Assert.True(CoreShellNotificationPresentation.ShouldShowToast(notification, now));
    }

    [Fact]
    public void NotificationPresentation_KeepsInProgressToastsUntilUpdatedOrDismissed()
    {
        var now = DateTimeOffset.UtcNow;
        var notification = CreateNotification(
            status: CoreShellNotificationStatus.InProgress,
            updatedAt: now.Subtract(TimeSpan.FromMinutes(5)),
            toastTimeToLive: TimeSpan.FromSeconds(8));

        Assert.True(CoreShellNotificationPresentation.ShouldShowToast(notification, now));
        Assert.False(CoreShellNotificationPresentation.ShouldShowToast(
            notification with { DismissedAt = now },
            now));
    }

    [Fact]
    public void NotificationPresentation_HonorsSuppressedScheduledAndAcknowledgedState()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.False(CoreShellNotificationPresentation.ShouldShowToast(
            CreateNotification(toastBehavior: CoreShellNotificationToastBehavior.Suppressed),
            now));
        Assert.False(CoreShellNotificationPresentation.ShouldShowToast(
            CreateNotification(visibleAt: now.AddSeconds(1)),
            now));
        Assert.False(CoreShellNotificationPresentation.ShouldShowToast(
            CreateNotification(acknowledgedAt: now),
            now));
    }

    [Fact]
    public void NotificationPresentation_KeepsNeverDismissAndUntilAcknowledgedToastsVisible()
    {
        var now = DateTimeOffset.UtcNow;
        var updatedAt = now.Subtract(TimeSpan.FromMinutes(5));

        Assert.True(CoreShellNotificationPresentation.ShouldShowToast(
            CreateNotification(
                updatedAt: updatedAt,
                toastAutoDismiss: CoreShellToastAutoDismissBehavior.Never),
            now));
        Assert.True(CoreShellNotificationPresentation.ShouldShowToast(
            CreateNotification(
                updatedAt: updatedAt,
                toastBehavior: CoreShellNotificationToastBehavior.UntilAcknowledged),
            now));
    }

    [Fact]
    public void NotificationPresentation_FiltersBeforeApplyingToastLimit()
    {
        var now = DateTimeOffset.UtcNow;
        var expired = Enumerable
            .Range(0, 20)
            .Select(index => CreateNotification(
                id: $"expired-{index}",
                updatedAt: now.Subtract(TimeSpan.FromMinutes(index + 1)),
                toastTimeToLive: TimeSpan.FromSeconds(1)));
        var inProgress = CreateNotification(
            id: "in-progress",
            status: CoreShellNotificationStatus.InProgress,
            updatedAt: now.Subtract(TimeSpan.FromHours(1)));

        var toasts = CoreShellNotificationPresentation.SelectToastItems(
            expired.Concat([inProgress]),
            now,
            maxItems: 1);

        var toast = Assert.Single(toasts);
        Assert.Equal("in-progress", toast.Id);
    }

    [Fact]
    public void NotificationPresentation_ExpiresPlainToastOnlyItemsAfterTimeToLive()
    {
        var now = DateTimeOffset.UtcNow;
        var toast = CreateToast(
            updatedAt: now.Subtract(TimeSpan.FromSeconds(9)),
            timeToLive: TimeSpan.FromSeconds(8));

        Assert.False(CoreShellNotificationPresentation.ShouldShowToast(toast, now));
    }

    [Fact]
    public void NotificationPresentation_KeepsInProgressToastOnlyItemsUntilUpdatedOrDismissed()
    {
        var now = DateTimeOffset.UtcNow;
        var toast = CreateToast(
            status: CoreShellNotificationStatus.InProgress,
            updatedAt: now.Subtract(TimeSpan.FromMinutes(5)),
            timeToLive: TimeSpan.Zero);

        Assert.True(CoreShellNotificationPresentation.ShouldShowToast(toast, now));
    }

    [Fact]
    public void NotificationPresentation_KeepsNeverAutoDismissToastOnlyItemsVisible()
    {
        var now = DateTimeOffset.UtcNow;
        var toast = CreateToast(
            updatedAt: now.Subtract(TimeSpan.FromMinutes(5)),
            timeToLive: TimeSpan.Zero,
            autoDismiss: CoreShellToastAutoDismissBehavior.Never);

        Assert.True(CoreShellNotificationPresentation.ShouldShowToast(toast, now));
    }

    [Fact]
    public void NotificationPresentation_FiltersToastOnlyItemsBeforeApplyingToastLimit()
    {
        var now = DateTimeOffset.UtcNow;
        var expired = Enumerable
            .Range(0, 20)
            .Select(index => CreateToast(
                id: $"expired-{index}",
                updatedAt: now.Subtract(TimeSpan.FromMinutes(index + 1)),
                timeToLive: TimeSpan.FromSeconds(1)));
        var inProgress = CreateToast(
            id: "in-progress",
            status: CoreShellNotificationStatus.InProgress,
            updatedAt: now.Subtract(TimeSpan.FromHours(1)));

        var toasts = CoreShellNotificationPresentation.SelectToastItems(
            expired.Concat([inProgress]),
            now,
            maxItems: 1);

        var toast = Assert.Single(toasts);
        Assert.Equal("in-progress", toast.Id);
    }

    [Fact]
    public void AddCoreShellBlazor_RegistersEmptyNotificationProducerByDefault()
    {
        var services = new ServiceCollection();

        services.AddCoreShellBlazor();

        var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<ICoreShellNotificationProducer>();
        Assert.IsType<EmptyCoreShellNotificationProducer>(service);
    }

    [Fact]
    public void AddCoreShellBlazor_PreservesHostNotificationProducer()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICoreShellNotificationProducer, TestNotificationProducer>();

        services.AddCoreShellBlazor();

        var provider = services.BuildServiceProvider();
        Assert.IsType<TestNotificationProducer>(provider.GetRequiredService<ICoreShellNotificationProducer>());
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
        Assert.Equal(CoreShellToastAutoDismissBehavior.AfterTimeToLive, toast.AutoDismiss);
        Assert.Equal(CoreShellToastDefaults.DefaultTimeToLive, toast.TimeToLive);
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
    public async Task EmptyToastService_CanReturnNeverAutoDismissReference()
    {
        ICoreShellToastService service = new EmptyCoreShellToastService();

        var toast = await service.PublishAsync(new CoreShellToastRequest(
            "Running",
            "The task is running.",
            CoreShellNotificationSeverity.Info,
            CoreShellNotificationStatus.InProgress,
            AutoDismiss: CoreShellToastAutoDismissBehavior.Never));

        Assert.Equal(CoreShellToastAutoDismissBehavior.Never, toast.AutoDismiss);
        Assert.Null(toast.TimeToLive);
    }

    [Fact]
    public async Task InMemoryToastService_PublishesUpdatesAndDismissesToastReferences()
    {
        ICoreShellToastService service = new InMemoryCoreShellToastService();

        var toast = await service.PublishAsync(new CoreShellToastRequest(
            "Running",
            "The task is running.",
            CoreShellNotificationSeverity.Info,
            CoreShellNotificationStatus.InProgress,
            Actions:
            [
                new CoreShellNotificationAction(
                    "open",
                    "Open",
                    new CoreShellNotificationTarget("/operations"),
                    IsPrimary: true)
            ],
            TimeToLive: TimeSpan.Zero));

        Assert.Collection(
            await service.GetToastsAsync(),
            item =>
            {
                Assert.Equal(toast.Id, item.Id);
                Assert.Equal(CoreShellNotificationStatus.InProgress, item.Status);
                Assert.Equal(CoreShellToastAutoDismissBehavior.AfterTimeToLive, item.AutoDismiss);
                Assert.Equal(TimeSpan.Zero, item.TimeToLive);
            });

        var updated = await service.UpdateAsync(
            toast.Id,
            new CoreShellToastUpdate(
                Title: "Completed",
                Message: "The task completed.",
                Severity: CoreShellNotificationSeverity.Success,
                Status: CoreShellNotificationStatus.Succeeded,
                Actions: []));

        Assert.NotNull(updated);
        Assert.Equal("Completed", updated.Title);
        Assert.Empty(await service.GetToastsAsync());

        var persistent = await service.PublishAsync(new CoreShellToastRequest(
            "Needs action",
            "Review this item.",
            AutoDismiss: CoreShellToastAutoDismissBehavior.Never,
            Actions:
            [
                new CoreShellNotificationAction("dismiss", "Dismiss")
            ]));

        await service.HandleActionAsync(persistent.Id, "dismiss");

        Assert.Empty(await service.GetToastsAsync());
    }

    [Fact]
    public async Task InMemoryToastService_ReplacesExistingToastWhenPublishingSameId()
    {
        ICoreShellToastService service = new InMemoryCoreShellToastService();
        var changes = new List<CoreShellToastChangeKind>();
        service.ToastsChanged += (_, args) => changes.Add(args.Kind);

        var first = await service.PublishAsync(new CoreShellToastRequest(
            "Exporting",
            "Exporting the current resource template.",
            CoreShellNotificationSeverity.Info,
            CoreShellNotificationStatus.Active,
            Id: "resource-template-export"));

        var second = await service.PublishAsync(new CoreShellToastRequest(
            "Exported",
            "The resource template was exported.",
            CoreShellNotificationSeverity.Success,
            CoreShellNotificationStatus.Succeeded,
            Id: "RESOURCE-TEMPLATE-EXPORT"));

        Assert.Equal(first.Id, second.Id, StringComparer.OrdinalIgnoreCase);
        Assert.Collection(
            await service.GetToastsAsync(),
            item =>
            {
                Assert.Equal(second.Id, item.Id);
                Assert.Equal("Exported", item.Title);
                Assert.Equal(CoreShellNotificationSeverity.Success, item.Severity);
                Assert.True(item.CreatedAt >= first.CreatedAt);
            });
        Assert.Equal(
            [CoreShellToastChangeKind.Published, CoreShellToastChangeKind.Updated],
            changes);
    }

    [Fact]
    public async Task InMemoryToastService_CompletedProgressToastReturnsToDefaultAutoDismiss()
    {
        ICoreShellToastService service = new InMemoryCoreShellToastService();

        var running = await service.PublishAsync(new CoreShellToastRequest(
            "Running",
            "The task is running.",
            CoreShellNotificationSeverity.Info,
            CoreShellNotificationStatus.InProgress,
            AutoDismiss: CoreShellToastAutoDismissBehavior.Never));

        var completed = await service.UpdateAsync(
            running.Id,
            new CoreShellToastUpdate(
                Title: "Completed",
                Message: "The task completed.",
                Severity: CoreShellNotificationSeverity.Success,
                Status: CoreShellNotificationStatus.Succeeded));

        Assert.NotNull(completed);
        Assert.Equal(CoreShellToastAutoDismissBehavior.AfterTimeToLive, completed.AutoDismiss);
        Assert.Equal(CoreShellToastDefaults.DefaultTimeToLive, completed.TimeToLive);
        Assert.True(CoreShellNotificationPresentation.ShouldShowToast(
            completed,
            completed.UpdatedAt.Add(CoreShellToastDefaults.DefaultTimeToLive).AddTicks(-1)));
        Assert.False(CoreShellNotificationPresentation.ShouldShowToast(
            completed,
            completed.UpdatedAt.Add(CoreShellToastDefaults.DefaultTimeToLive)));
    }

    [Fact]
    public async Task InMemoryToastService_ValidatesInputs()
    {
        ICoreShellToastService service = new InMemoryCoreShellToastService();

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

    private static CoreShellNotificationInstance CreateNotification(
        string id = "notification-1",
        CoreShellNotificationStatus status = CoreShellNotificationStatus.Active,
        DateTimeOffset? updatedAt = null,
        TimeSpan? toastTimeToLive = null,
        CoreShellToastAutoDismissBehavior toastAutoDismiss = CoreShellToastAutoDismissBehavior.AfterTimeToLive,
        CoreShellNotificationToastBehavior toastBehavior = CoreShellNotificationToastBehavior.Default,
        DateTimeOffset? visibleAt = null,
        DateTimeOffset? acknowledgedAt = null) =>
        new(
            id,
            "Notification",
            "Notification message.",
            CoreShellNotificationSeverity.Info,
            status,
            DateTimeOffset.UtcNow,
            updatedAt ?? DateTimeOffset.UtcNow,
            AcknowledgedAt: acknowledgedAt,
            ToastBehavior: toastBehavior,
            ToastTimeToLive: toastTimeToLive,
            ToastAutoDismiss: toastAutoDismiss,
            VisibleAt: visibleAt);

    private static CoreShellToast CreateToast(
        string id = "toast-1",
        CoreShellNotificationStatus status = CoreShellNotificationStatus.Active,
        DateTimeOffset? updatedAt = null,
        TimeSpan? timeToLive = null,
        CoreShellToastAutoDismissBehavior autoDismiss = CoreShellToastAutoDismissBehavior.AfterTimeToLive) =>
        new(
            id,
            "Toast",
            "Toast message.",
            CoreShellNotificationSeverity.Info,
            status,
            DateTimeOffset.UtcNow,
            updatedAt ?? DateTimeOffset.UtcNow,
            TimeToLive: timeToLive,
            AutoDismiss: autoDismiss);

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

    private sealed class TestNotificationProducer : ICoreShellNotificationProducer
    {
        public Task<CoreShellNotificationInstance> PublishAsync(
            CoreShellNotificationRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CoreShellNotificationInstance(
                "notification-1",
                request.Title,
                request.Message,
                request.Severity,
                request.Status,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow));

        public Task<CoreShellNotificationInstance?> UpdateAsync(
            string notificationId,
            CoreShellNotificationUpdate update,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<CoreShellNotificationInstance?>(null);

        public Task DismissAsync(
            string notificationId,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
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
