using CloudShell.Abstractions.Logs;

namespace CloudShell.Abstractions.Tests;

public sealed class LogSourceTests
{
    [Fact]
    public void LogProvider_CanExposeLogSourcesWithoutDescriptors()
    {
        ILogProvider provider = new SourceFirstLogProvider();

        var source = Assert.Single(provider.GetLogSources());

        Assert.Equal("source:first", source.Id);
        Assert.Equal("Source first", source.Name);
    }

    private sealed class SourceFirstLogProvider : ILogProvider
    {
        public string Id => "source-first";

        public string DisplayName => "Source first";

        public IReadOnlyList<LogSource> GetLogSources() =>
        [
            new(
                "source:first",
                "Source first",
                DisplayName,
                "source",
                LogSourceKind.Provider,
                ResourceLogSourceKind.ProviderDefined)
        ];

        public Task<IReadOnlyList<LogEntry>> ReadLogAsync(
            string logId,
            int maxEntries = 200,
            DateTimeOffset? before = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LogEntry>>([]);
    }

}
