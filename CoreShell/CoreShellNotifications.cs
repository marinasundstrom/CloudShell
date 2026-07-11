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
