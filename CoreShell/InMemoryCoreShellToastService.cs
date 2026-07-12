namespace CoreShell;

public sealed class InMemoryCoreShellToastService : ICoreShellToastService
{
    private readonly object _gate = new();
    private readonly List<CoreShellToast> _toasts = [];

    public event EventHandler<CoreShellToastsChangedEventArgs>? ToastsChanged;

    public Task<IReadOnlyList<CoreShellToast>> GetToastsAsync(
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            RemoveExpired(DateTimeOffset.UtcNow);
            return Task.FromResult<IReadOnlyList<CoreShellToast>>(
                _toasts
                    .OrderByDescending(toast => toast.UpdatedAt)
                    .ThenByDescending(toast => toast.CreatedAt)
                    .ToArray());
        }
    }

    public Task<CoreShellToast> PublishAsync(
        CoreShellToastRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = DateTimeOffset.UtcNow;
        var autoDismiss = NormalizeAutoDismiss(request.AutoDismiss);
        var toast = new CoreShellToast(
            NormalizeOptional(request.Id) ?? Guid.NewGuid().ToString("n"),
            NormalizeRequired(request.Title, nameof(request.Title)),
            NormalizeRequired(request.Message, nameof(request.Message)),
            request.Severity,
            request.Status,
            now,
            now,
            Source: NormalizeOptional(request.Source),
            Target: request.Target,
            Actions: NormalizeActions(request.Actions),
            TimeToLive: autoDismiss == CoreShellToastAutoDismissBehavior.Never
                ? null
                : request.TimeToLive ?? CoreShellToastDefaults.DefaultTimeToLive,
            AutoDismiss: autoDismiss);

        var changeKind = CoreShellToastChangeKind.Published;
        lock (_gate)
        {
            var existingIndex = _toasts.FindIndex(item =>
                string.Equals(item.Id, toast.Id, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
            {
                _toasts.RemoveAll(item =>
                    string.Equals(item.Id, toast.Id, StringComparison.OrdinalIgnoreCase));
                changeKind = CoreShellToastChangeKind.Updated;
            }

            _toasts.Add(toast);
        }

        RaiseChanged(changeKind, toast.Id);
        return Task.FromResult(toast);
    }

    public Task<CoreShellToast?> UpdateAsync(
        string toastId,
        CoreShellToastUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toastId);
        ArgumentNullException.ThrowIfNull(update);

        CoreShellToast? toast = null;
        lock (_gate)
        {
            var index = _toasts.FindIndex(item =>
                string.Equals(item.Id, toastId, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return Task.FromResult<CoreShellToast?>(null);
            }

            var current = _toasts[index];
            var status = update.Status ?? current.Status;
            var autoDismiss = update.AutoDismiss.HasValue
                ? NormalizeAutoDismiss(update.AutoDismiss.Value)
                : GetAutoDismissForUpdate(current, status);
            TimeSpan? timeToLive = autoDismiss == CoreShellToastAutoDismissBehavior.Never
                ? null
                : update.TimeToLive ?? current.TimeToLive ?? CoreShellToastDefaults.DefaultTimeToLive;

            toast = current with
            {
                Title = NormalizeOptional(update.Title) ?? current.Title,
                Message = NormalizeOptional(update.Message) ?? current.Message,
                Severity = update.Severity ?? current.Severity,
                Status = status,
                Target = update.Target ?? current.Target,
                Actions = update.Actions is null
                    ? current.Actions
                    : NormalizeActions(update.Actions),
                TimeToLive = timeToLive,
                AutoDismiss = autoDismiss,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _toasts[index] = toast;
        }

        RaiseChanged(CoreShellToastChangeKind.Updated, toast.Id);
        return Task.FromResult<CoreShellToast?>(toast);
    }

    public Task HandleActionAsync(
        string toastId,
        string actionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toastId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);

        var changed = false;
        lock (_gate)
        {
            var matchingToast = _toasts.FirstOrDefault(item =>
                string.Equals(item.Id, toastId, StringComparison.OrdinalIgnoreCase));
            if (matchingToast?.Actions?.Any(item =>
                    string.Equals(item.Id, actionId, StringComparison.OrdinalIgnoreCase)) == true)
            {
                changed = _toasts.RemoveAll(item =>
                    string.Equals(item.Id, toastId, StringComparison.OrdinalIgnoreCase)) > 0;
            }
        }

        if (changed)
        {
            RaiseChanged(CoreShellToastChangeKind.Dismissed, toastId);
        }

        return Task.CompletedTask;
    }

    public Task DismissAsync(
        string toastId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toastId);

        var changed = false;
        lock (_gate)
        {
            changed = _toasts.RemoveAll(item =>
                string.Equals(item.Id, toastId, StringComparison.OrdinalIgnoreCase)) > 0;
        }

        if (changed)
        {
            RaiseChanged(CoreShellToastChangeKind.Dismissed, toastId);
        }

        return Task.CompletedTask;
    }

    private void RemoveExpired(DateTimeOffset now)
    {
        var removed = _toasts.RemoveAll(toast =>
            !CoreShellNotificationPresentation.ShouldShowToast(toast, now));

        if (removed > 0)
        {
            RaiseChanged(CoreShellToastChangeKind.RefreshRequired);
        }
    }

    private void RaiseChanged(CoreShellToastChangeKind kind, string? toastId = null) =>
        ToastsChanged?.Invoke(this, new CoreShellToastsChangedEventArgs(kind, toastId));

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

    private static CoreShellToastAutoDismissBehavior GetAutoDismissForUpdate(
        CoreShellToast current,
        CoreShellNotificationStatus status)
    {
        if (current.Status == CoreShellNotificationStatus.InProgress &&
            status != CoreShellNotificationStatus.InProgress &&
            current.AutoDismiss == CoreShellToastAutoDismissBehavior.Never)
        {
            return CoreShellToastAutoDismissBehavior.AfterTimeToLive;
        }

        return current.AutoDismiss;
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
