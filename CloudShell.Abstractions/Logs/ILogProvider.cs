namespace CloudShell.Abstractions.Logs;

public interface ILogProvider : ILogSourceContributor
{
    string Id { get; }

    string DisplayName { get; }

    IReadOnlyList<LogSource> ILogSourceContributor.GetLogSources() =>
        GetLogSources();

    new IReadOnlyList<LogSource> GetLogSources();

    bool CanOpenLogSource(LogSource source) =>
        GetLogSources()
            .Any(candidate => string.Equals(candidate.Id, source.Id, StringComparison.OrdinalIgnoreCase));

    ValueTask<ILogSourceSession?> OpenLogSourceAsync(
        LogSource source,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return OpenLogSourceAsync(source.Id, cancellationToken);
    }

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

    Task<IReadOnlyList<LogEntry>> ReadLogSourceAsync(
        string logSourceId,
        int maxEntries = 200,
        DateTimeOffset? before = null,
        CancellationToken cancellationToken = default);

    async IAsyncEnumerable<LogEntry> StreamLogSourceAsync(
        string logSourceId,
        int initialEntries = 50,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (initialEntries <= 0)
        {
            yield break;
        }

        var entries = await ReadLogSourceAsync(logSourceId, initialEntries, cancellationToken: cancellationToken);
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return entry;
        }
    }
}
