namespace CloudShell.Abstractions.Logs;

public interface ILogStore
{
    IReadOnlyList<ILogProvider> Providers { get; }

    IReadOnlyList<LogSource> GetLogSources();

    LogSource? GetLogSource(string logSourceId) =>
        GetLogSources()
            .FirstOrDefault(source => string.Equals(source.Id, logSourceId, StringComparison.OrdinalIgnoreCase));

    Task<IReadOnlyList<LogEntry>> ReadLogSourceAsync(
        string logSourceId,
        int maxEntries = 200,
        DateTimeOffset? before = null,
        CancellationToken cancellationToken = default);

    ValueTask<ILogSourceSession?> OpenLogSourceSessionAsync(
        string logSourceId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<ILogSourceSession?>(null);

    IAsyncEnumerable<LogEntry> StreamLogSourceAsync(
        string logSourceId,
        int initialEntries = 50,
        CancellationToken cancellationToken = default);
}
