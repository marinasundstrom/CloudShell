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
        CancellationToken cancellationToken = default);
}
