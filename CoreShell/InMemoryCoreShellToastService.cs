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

            toast = _toasts[index] with
            {
                Title = NormalizeOptional(update.Title) ?? _toasts[index].Title,
                Message = NormalizeOptional(update.Message) ?? _toasts[index].Message,
                Severity = update.Severity ?? _toasts[index].Severity,
                Status = update.Status ?? _toasts[index].Status,
                Target = update.Target ?? _toasts[index].Target,
                Actions = update.Actions is null
                    ? _toasts[index].Actions
                    : NormalizeActions(update.Actions),
                TimeToLive = update.TimeToLive ?? _toasts[index].TimeToLive,
                AutoDismiss = update.AutoDismiss.HasValue
                    ? NormalizeAutoDismiss(update.AutoDismiss.Value)
                    : _toasts[index].AutoDismiss,
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
