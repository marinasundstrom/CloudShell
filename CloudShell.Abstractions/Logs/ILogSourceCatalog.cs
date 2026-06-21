namespace CloudShell.Abstractions.Logs;

public interface ILogSourceContributor
{
    IReadOnlyList<LogSource> GetLogSources();
}

public interface ILogSourceCatalog : ILogSourceContributor
{
    LogSource? GetLogSource(string logSourceId) =>
        GetLogSources()
            .FirstOrDefault(source => string.Equals(source.Id, logSourceId, StringComparison.OrdinalIgnoreCase));
}
