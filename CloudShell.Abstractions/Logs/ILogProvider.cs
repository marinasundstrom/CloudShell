namespace CloudShell.Abstractions.Logs;

public interface ILogProvider
{
    string Id { get; }

    string DisplayName { get; }

    IReadOnlyList<LogDescriptor> GetLogs();

    IReadOnlyList<LogSource> GetLogSources() =>
        GetLogs()
            .Select(log => log.ToLogSource())
            .ToArray();

    ValueTask<ILogSourceSession?> OpenLogSourceAsync(
        string logSourceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = GetLogSources().FirstOrDefault(source =>
            string.Equals(source.Id, logSourceId, StringComparison.OrdinalIgnoreCase));

        return ValueTask.FromResult<ILogSourceSession?>(
            source is null ? null : new DelegatingLogSourceSession(this, source.Id));
    }

    Task<IReadOnlyList<LogEntry>> ReadLogAsync(
        string logId,
        int maxEntries = 200,
        DateTimeOffset? before = null,
        CancellationToken cancellationToken = default);

    async IAsyncEnumerable<LogEntry> StreamLogAsync(
        string logId,
        int initialEntries = 50,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (initialEntries <= 0)
        {
            yield break;
        }

        var entries = await ReadLogAsync(logId, initialEntries, cancellationToken: cancellationToken);
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return entry;
        }
    }
}
