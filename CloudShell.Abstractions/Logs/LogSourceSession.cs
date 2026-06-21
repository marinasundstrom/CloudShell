namespace CloudShell.Abstractions.Logs;

public interface ILogSourceSession : IAsyncDisposable
{
    string SourceId { get; }

    Task<IReadOnlyList<LogEntry>> ReadAsync(
        int maxEntries = 200,
        DateTimeOffset? before = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<LogEntry> StreamAsync(
        int initialEntries = 50,
        CancellationToken cancellationToken = default);
}

internal sealed class DelegatingLogSourceSession(
    ILogProvider provider,
    string sourceId) : ILogSourceSession
{
    public string SourceId => sourceId;

    public Task<IReadOnlyList<LogEntry>> ReadAsync(
        int maxEntries = 200,
        DateTimeOffset? before = null,
        CancellationToken cancellationToken = default) =>
        provider.ReadLogAsync(sourceId, maxEntries, before, cancellationToken);

    public IAsyncEnumerable<LogEntry> StreamAsync(
        int initialEntries = 50,
        CancellationToken cancellationToken = default) =>
        provider.StreamLogAsync(sourceId, initialEntries, cancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
