namespace CoreShell;

public enum CoreShellNotificationSeverity
{
    Info = 0,
    Success = 1,
    Warning = 2,
    Error = 3
}

public enum CoreShellNotificationStatus
{
    Active = 0,
    InProgress = 1,
    Succeeded = 2,
    Failed = 3,
    NeedsAttention = 4
}

public enum CoreShellNotificationChangeKind
{
    RefreshRequired = 0,
    Created = 1,
    Updated = 2,
    Acknowledged = 3,
    Dismissed = 4
}

public enum CoreShellNotificationToastBehavior
{
    Default = 0,
    Suppressed = 1,
    UntilAcknowledged = 2
}

public enum CoreShellToastChangeKind
{
    RefreshRequired = 0,
    Published = 1,
    Updated = 2,
    Dismissed = 3
}

public sealed record CoreShellNotificationQuery(
    bool IncludeDismissed = false,
    int? Limit = null);

public sealed record CoreShellNotificationTarget(
    string Href,
    string? Label = null);

public sealed record CoreShellNotificationAction(
    string Id,
    string Label,
    CoreShellNotificationTarget? Target = null,
    bool IsPrimary = false);

public sealed record CoreShellToastRequest(
    string Title,
    string Message,
    CoreShellNotificationSeverity Severity = CoreShellNotificationSeverity.Info,
    CoreShellNotificationStatus Status = CoreShellNotificationStatus.Active,
    string? Source = null,
    CoreShellNotificationTarget? Target = null,
    IReadOnlyList<CoreShellNotificationAction>? Actions = null,
    TimeSpan? TimeToLive = null,
    string? Id = null);

public sealed record CoreShellToastUpdate(
    string? Title = null,
    string? Message = null,
    CoreShellNotificationSeverity? Severity = null,
    CoreShellNotificationStatus? Status = null,
    CoreShellNotificationTarget? Target = null,
    IReadOnlyList<CoreShellNotificationAction>? Actions = null);

public sealed record CoreShellToast(
    string Id,
    string Title,
    string Message,
    CoreShellNotificationSeverity Severity,
    CoreShellNotificationStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? Source = null,
    CoreShellNotificationTarget? Target = null,
    IReadOnlyList<CoreShellNotificationAction>? Actions = null,
    TimeSpan? TimeToLive = null);

public sealed record CoreShellNotificationInstance(
    string Id,
    string Title,
    string Message,
    CoreShellNotificationSeverity Severity,
    CoreShellNotificationStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? Source = null,
    CoreShellNotificationTarget? Target = null,
    string? EventId = null,
    DateTimeOffset? ReadAt = null,
    DateTimeOffset? AcknowledgedAt = null,
    DateTimeOffset? DismissedAt = null,
    IReadOnlyDictionary<string, string>? Attributes = null,
    IReadOnlyList<CoreShellNotificationAction>? Actions = null,
    CoreShellNotificationToastBehavior ToastBehavior = CoreShellNotificationToastBehavior.Default);

public sealed class CoreShellNotificationsChangedEventArgs(
    CoreShellNotificationChangeKind kind = CoreShellNotificationChangeKind.RefreshRequired,
    string? notificationId = null) : EventArgs
{
    public CoreShellNotificationChangeKind Kind { get; } = kind;

    public string? NotificationId { get; } = notificationId;
}

public sealed class CoreShellToastsChangedEventArgs(
    CoreShellToastChangeKind kind = CoreShellToastChangeKind.RefreshRequired,
    string? toastId = null) : EventArgs
{
    public CoreShellToastChangeKind Kind { get; } = kind;

    public string? ToastId { get; } = toastId;
}

public interface ICoreShellNotificationService
{
    event EventHandler<CoreShellNotificationsChangedEventArgs>? NotificationsChanged;

    Task<IReadOnlyList<CoreShellNotificationInstance>> GetNotificationsAsync(
        CoreShellNotificationQuery? query = null,
        CancellationToken cancellationToken = default);

    Task AcknowledgeAsync(
        string notificationId,
        CancellationToken cancellationToken = default);

    Task HandleActionAsync(
        string notificationId,
        string actionId,
        CancellationToken cancellationToken = default);

    Task DismissAsync(
        string notificationId,
        CancellationToken cancellationToken = default);
}

public interface ICoreShellToastService
{
    event EventHandler<CoreShellToastsChangedEventArgs>? ToastsChanged;

    Task<IReadOnlyList<CoreShellToast>> GetToastsAsync(
        CancellationToken cancellationToken = default);

    Task<CoreShellToast> PublishAsync(
        CoreShellToastRequest request,
        CancellationToken cancellationToken = default);

    Task<CoreShellToast?> UpdateAsync(
        string toastId,
        CoreShellToastUpdate update,
        CancellationToken cancellationToken = default);

    Task HandleActionAsync(
        string toastId,
        string actionId,
        CancellationToken cancellationToken = default);

    Task DismissAsync(
        string toastId,
        CancellationToken cancellationToken = default);
}

public sealed class EmptyCoreShellNotificationService : ICoreShellNotificationService
{
    public event EventHandler<CoreShellNotificationsChangedEventArgs>? NotificationsChanged
    {
        add { }
        remove { }
    }

    public Task<IReadOnlyList<CoreShellNotificationInstance>> GetNotificationsAsync(
        CoreShellNotificationQuery? query = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CoreShellNotificationInstance>>([]);

    public Task AcknowledgeAsync(
        string notificationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(notificationId);
        return Task.CompletedTask;
    }

    public Task HandleActionAsync(
        string notificationId,
        string actionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(notificationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        return Task.CompletedTask;
    }

    public Task DismissAsync(
        string notificationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(notificationId);
        return Task.CompletedTask;
    }
}

public sealed class EmptyCoreShellToastService : ICoreShellToastService
{
    public event EventHandler<CoreShellToastsChangedEventArgs>? ToastsChanged
    {
        add { }
        remove { }
    }

    public Task<IReadOnlyList<CoreShellToast>> GetToastsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CoreShellToast>>([]);

    public Task<CoreShellToast> PublishAsync(
        CoreShellToastRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = DateTimeOffset.UtcNow;
        return Task.FromResult(new CoreShellToast(
            request.Id ?? Guid.NewGuid().ToString("n"),
            NormalizeRequired(request.Title, nameof(request.Title)),
            NormalizeRequired(request.Message, nameof(request.Message)),
            request.Severity,
            request.Status,
            now,
            now,
            Source: NormalizeOptional(request.Source),
            Target: request.Target,
            Actions: request.Actions,
            TimeToLive: request.TimeToLive));
    }

    public Task<CoreShellToast?> UpdateAsync(
        string toastId,
        CoreShellToastUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toastId);
        ArgumentNullException.ThrowIfNull(update);
        return Task.FromResult<CoreShellToast?>(null);
    }

    public Task HandleActionAsync(
        string toastId,
        string actionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toastId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        return Task.CompletedTask;
    }

    public Task DismissAsync(
        string toastId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toastId);
        return Task.CompletedTask;
    }

    private static string NormalizeRequired(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
