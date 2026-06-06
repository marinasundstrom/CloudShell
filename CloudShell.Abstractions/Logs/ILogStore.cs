namespace CloudShell.Abstractions.Logs;

public interface ILogStore
{
    IReadOnlyList<ILogProvider> Providers { get; }

    IReadOnlyList<LogDescriptor> GetLogs();

    IReadOnlyList<LogDescriptor> GetLogsForResource(string resourceId);

    LogDescriptor? GetLog(string logId);

    Task<IReadOnlyList<LogEntry>> ReadLogAsync(
        string logId,
        int maxEntries = 200,
        DateTimeOffset? before = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<LogEntry> StreamLogAsync(
        string logId,
        int initialEntries = 50,
        CancellationToken cancellationToken = default);
}
