namespace CloudShell.Abstractions.Logs;

public interface ILogProvider
{
    string Id { get; }

    string DisplayName { get; }

    IReadOnlyList<LogDescriptor> GetLogs();

    Task<IReadOnlyList<LogEntry>> ReadLogAsync(
        string logId,
        int maxEntries = 200,
        CancellationToken cancellationToken = default);
}
