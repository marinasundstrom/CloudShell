namespace CloudShell.Abstractions.Logs;

public interface ILogStore
{
    IReadOnlyList<ILogProvider> Providers { get; }

    IReadOnlyList<LogDescriptor> GetLogs();

    IReadOnlyList<LogSource> GetLogSources();

    IReadOnlyList<LogDescriptor> GetLogsForResource(string resourceId);

    LogDescriptor? GetLog(string logId);

    LogSource? GetLogSource(string logSourceId) =>
        GetLogSources()
            .FirstOrDefault(source => string.Equals(source.Id, logSourceId, StringComparison.OrdinalIgnoreCase));

    Task<IReadOnlyList<LogEntry>> ReadLogSourceAsync(
        string logSourceId,
        int maxEntries = 200,
        DateTimeOffset? before = null,
        CancellationToken cancellationToken = default) =>
        ReadLogAsync(logSourceId, maxEntries, before, cancellationToken);

    Task<IReadOnlyList<LogEntry>> ReadLogAsync(
        string logId,
        int maxEntries = 200,
        DateTimeOffset? before = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<LogEntry> StreamLogSourceAsync(
        string logSourceId,
        int initialEntries = 50,
        CancellationToken cancellationToken = default) =>
        StreamLogAsync(logSourceId, initialEntries, cancellationToken);

    IAsyncEnumerable<LogEntry> StreamLogAsync(
        string logId,
        int initialEntries = 50,
        CancellationToken cancellationToken = default);
}
