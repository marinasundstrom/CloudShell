namespace CloudShell.Abstractions.Logs;

public interface ILogStore
{
    IReadOnlyList<ILogProvider> Providers { get; }

    IReadOnlyList<LogSource> GetLogSources();

    LogSource? GetLogSource(string logSourceId) =>
        GetLogSources()
            .FirstOrDefault(source => string.Equals(source.Id, logSourceId, StringComparison.OrdinalIgnoreCase));

    ValueTask<ILogSession?> OpenLogSessionAsync(
        IReadOnlyList<string> logSourceIds,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<ILogSession?>(null);
}
