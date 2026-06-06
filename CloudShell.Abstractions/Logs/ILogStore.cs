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
        CancellationToken cancellationToken = default);
}
