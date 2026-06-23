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
        Assert.Empty(provider.GetLogs());
    }

    [Fact]
    public void LogProvider_ProjectsLegacyDescriptorsToLogSources()
    {
        ILogProvider provider = new DescriptorBackedLogProvider();

        var source = Assert.Single(provider.GetLogSources());

        Assert.Equal("descriptor:legacy", source.Id);
        Assert.Equal(ResourceLogSourceKind.ProcessOutput, source.Kind);
        Assert.True(source.SupportsStreaming);
    }

    [Fact]
    public void LogDescriptor_ProjectsToLogSourceWithSourceMetadata()
    {
        var descriptor = new LogDescriptor(
            "application:test:logs",
            "Console logs",
            "Applications",
            "Test API",
            LogSourceKind.Resource,
            ResourceId: "application:test",
            SupportsStreaming: true,
            Description: "Application stdout and stderr.",
            Kind: ResourceLogSourceKind.ProcessOutput,
            Format: LogFormat.JsonConsole,
            Storage: LogStorage.InMemory,
            Capabilities: LogSourceCapabilities.Read | LogSourceCapabilities.StructuredFields,
            ProducerResourceId: "application:test",
            Origin: ResourceLogSourceOrigin.Programmatic,
            Configuration: new LogSourceConfiguration(
                IsConfigurable: true,
                SchemaId: "application.processOutput"),
            Purpose: ResourceLogSourcePurpose.Custom,
            Availability: LogSourceAvailability.ProducerRunning);

        var source = descriptor.ToLogSource();

        Assert.Equal(descriptor.Id, source.Id);
        Assert.Equal(ResourceLogSourceKind.ProcessOutput, source.Kind);
        Assert.Equal(LogFormat.JsonConsole, source.Format);
        Assert.Equal(LogStorageKind.InMemory, source.Storage.Kind);
        Assert.True(source.SupportsStreaming);
        Assert.True(source.Capabilities.HasFlag(LogSourceCapabilities.StructuredFields));
        Assert.Equal("application:test", source.ResourceId);
        Assert.Equal("application:test", source.ProducerResourceId);
        Assert.Equal(ResourceLogSourceOrigin.Programmatic, source.Origin);
        Assert.True(source.Configuration.IsConfigurable);
        Assert.Equal("application.processOutput", source.Configuration.SchemaId);
        Assert.Equal(ResourceLogSourcePurpose.Custom, source.Purpose);
        Assert.Equal(LogSourceAvailability.ProducerRunning, source.Availability);
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

    private sealed class DescriptorBackedLogProvider : ILogProvider
    {
        public string Id => "descriptor-backed";

        public string DisplayName => "Descriptor backed";

        public IReadOnlyList<LogDescriptor> GetLogs() =>
        [
            new(
                "descriptor:legacy",
                "Legacy descriptor",
                DisplayName,
                "legacy",
                LogSourceKind.Provider,
                SupportsStreaming: true,
                Kind: ResourceLogSourceKind.ProcessOutput)
        ];

        public Task<IReadOnlyList<LogEntry>> ReadLogAsync(
            string logId,
            int maxEntries = 200,
            DateTimeOffset? before = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LogEntry>>([]);
    }
}
