namespace CloudShell.Abstractions.Logs;

/// <summary>
/// Provides provider-owned access to one resolved log source.
/// </summary>
/// <remarks>
/// <see cref="ReadAsync"/> returns a bounded snapshot of entries available at
/// read time. <see cref="StreamAsync"/> additionally follows new entries only
/// when the source advertises <see cref="LogSourceCapabilities.Stream"/>.
/// Storage, retention, and the physical read or follow mechanism remain owned
/// by the provider or its backing system.
/// </remarks>
public interface ILogSourceSession : IAsyncDisposable
{
    string Id { get; }

    string SourceId { get; }

    LogSourceSessionStatus Status { get; }

    Task<IReadOnlyList<LogEntry>> ReadAsync(
        int maxEntries = 200,
        DateTimeOffset? before = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<LogEntry> StreamAsync(
        int initialEntries = 50,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Adapts provider-owned snapshot and optional live-follow operations to an
/// explicit source session.
/// </summary>
public sealed class DelegateLogSourceSession(
    string sourceId,
    Func<int, DateTimeOffset?, CancellationToken, Task<IReadOnlyList<LogEntry>>> read,
    Func<int, CancellationToken, IAsyncEnumerable<LogEntry>>? stream = null,
    Func<ValueTask>? dispose = null) : ILogSourceSession
{
    private int status = (int)LogSourceSessionStatus.Active;

    public string Id { get; } = Guid.NewGuid().ToString("N");

    public string SourceId => sourceId;

    public LogSourceSessionStatus Status =>
        (LogSourceSessionStatus)Volatile.Read(ref status);

    public Task<IReadOnlyList<LogEntry>> ReadAsync(
        int maxEntries = 200,
        DateTimeOffset? before = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Status == LogSourceSessionStatus.Closed, this);
        return read(maxEntries, before, cancellationToken);
    }

    public IAsyncEnumerable<LogEntry> StreamAsync(
        int initialEntries = 50,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Status == LogSourceSessionStatus.Closed, this);
        return stream?.Invoke(initialEntries, cancellationToken) ?? AsyncEnumerable.Empty<LogEntry>();
    }

    public async ValueTask DisposeAsync()
    {
        if ((LogSourceSessionStatus)Interlocked.Exchange(
                ref status,
                (int)LogSourceSessionStatus.Closed) != LogSourceSessionStatus.Closed &&
            dispose is not null)
        {
            await dispose();
        }
    }
}

public enum LogSourceSessionStatus
{
    Opening,
    Active,
    Idle,
    Closed,
    Failed
}
