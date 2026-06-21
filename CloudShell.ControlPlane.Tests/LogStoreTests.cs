using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Logs;

namespace CloudShell.ControlPlane.Tests;

public sealed class LogStoreTests
{
    private static readonly LogEntry ProviderProjectedEntry = new(
        DateTimeOffset.Parse("2026-06-21T12:00:00Z"),
        "Provider projected log entry",
        "Information",
        "Provider diagnostics");

    [Fact]
    public void GetLogSources_IncludesProviderProjectedSources()
    {
        var resource = CreateResource(
            "application:api",
            "api",
            logSources:
            [
                new ResourceLogSource(
                    "console",
                    "Console logs",
                    ResourceLogSourceKind.ProcessOutput,
                    Format: LogFormat.JsonConsole,
                    Capabilities: LogSourceCapabilities.Read,
                    Origin: ResourceLogSourceOrigin.ProviderDefault,
                    Purpose: ResourceLogSourcePurpose.Default)
            ]);
        var provider = new TestLogProvider(
            logs:
            [
                new LogDescriptor(
                    "application:api:logs",
                    "Console logs",
                    "Applications",
                    "api",
                    LogSourceKind.Resource,
                    ResourceId: resource.Id,
                    SupportsStreaming: true,
                    Kind: ResourceLogSourceKind.ProcessOutput,
                    Format: LogFormat.JsonConsole,
                    Capabilities: LogSourceCapabilities.Read | LogSourceCapabilities.Stream)
            ],
            sources:
            [
                new LogSource(
                    "provider:diagnostics",
                    "Provider diagnostics",
                    "Applications",
                    "Applications",
                    LogSourceKind.Provider,
                    ResourceLogSourceKind.ProviderDefined,
                    Capabilities: LogSourceCapabilities.Read,
                    Origin: ResourceLogSourceOrigin.ProviderProjected)
            ]);
        var store = new LogStore(
            [provider],
            new TestResourceManagerStore([resource]),
            new CloudShellExtensionRegistry(),
            new InMemoryCloudShellExtensionActivationStore());

        var sources = store.GetLogSources();

        var console = Assert.Single(sources, source => source.Name == "Console logs");
        Assert.Equal("application:api:logs", console.Id);
        Assert.Equal(resource.Id, console.ResourceId);
        Assert.True(console.SupportsStreaming);
        var diagnostics = Assert.Single(sources, source => source.Name == "Provider diagnostics");
        Assert.Equal("provider:diagnostics", diagnostics.Id);
        Assert.Null(diagnostics.ResourceId);
        Assert.Equal(LogSourceKind.Provider, diagnostics.SourceKind);
    }

    [Fact]
    public async Task ReadLogAsync_UsesProjectedLogSourceSession()
    {
        var provider = new TestLogProvider(
            logs: [],
            sources:
            [
                new LogSource(
                    "provider:diagnostics",
                    "Provider diagnostics",
                    "Applications",
                    "Applications",
                    LogSourceKind.Provider,
                    ResourceLogSourceKind.ProviderDefined,
                    Capabilities: LogSourceCapabilities.Read,
                    Origin: ResourceLogSourceOrigin.ProviderProjected)
            ]);
        var store = new LogStore(
            [provider],
            new TestResourceManagerStore([]),
            new CloudShellExtensionRegistry(),
            new InMemoryCloudShellExtensionActivationStore());

        var entries = await store.ReadLogAsync("provider:diagnostics");

        var entry = Assert.Single(entries);
        Assert.Equal(ProviderProjectedEntry.Message, entry.Message);
    }

    private static Resource CreateResource(
        string id,
        string name,
        IReadOnlyList<ResourceLogSource>? logSources = null) =>
        new(
            id,
            name,
            "Application",
            "Applications",
            "local",
            ResourceState.Running,
            [],
            "1.0",
            DateTimeOffset.UtcNow,
            [],
            LogSources: logSources);

    private sealed class TestLogProvider(
        IReadOnlyList<LogDescriptor> logs,
        IReadOnlyList<LogSource> sources) : ILogProvider
    {
        public string Id => "test";

        public string DisplayName => "Test";

        public IReadOnlyList<LogDescriptor> GetLogs() => logs;

        public IReadOnlyList<LogSource> GetLogSources() =>
            [.. logs.Select(log => log.ToLogSource()), .. sources];

        public ValueTask<ILogSourceSession?> OpenLogSourceAsync(
            string logSourceId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ILogSourceSession?>(
                sources.Any(source => string.Equals(source.Id, logSourceId, StringComparison.OrdinalIgnoreCase))
                    ? new TestLogSourceSession(logSourceId)
                    : null);

        public Task<IReadOnlyList<LogEntry>> ReadLogAsync(
            string logId,
            int maxEntries = 200,
            DateTimeOffset? before = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LogEntry>>([]);
    }

    private sealed class TestLogSourceSession(string sourceId) : ILogSourceSession
    {
        public string SourceId => sourceId;

        public Task<IReadOnlyList<LogEntry>> ReadAsync(
            int maxEntries = 200,
            DateTimeOffset? before = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LogEntry>>([ProviderProjectedEntry]);

        public async IAsyncEnumerable<LogEntry> StreamAsync(
            int initialEntries = 50,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (initialEntries <= 0)
            {
                yield break;
            }

            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return ProviderProjectedEntry;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestResourceManagerStore(IReadOnlyList<Resource> resources) : IResourceManagerStore
    {
        public IReadOnlyList<IResourceProvider> Providers => [];

        public IReadOnlyList<ResourceGroup> GetResourceGroups() => [];

        public IReadOnlyList<Resource> GetAvailableResources() => resources;

        public IReadOnlyList<Resource> GetResources() => resources;

        public IReadOnlyList<ResourceModelDiagnostic> GetResourceModelDiagnostics() => [];

        public ResourceClass? GetResourceTypeClass(string resourceType) => null;

        public Resource? GetResource(string id) =>
            resources.FirstOrDefault(resource => string.Equals(resource.Id, id, StringComparison.OrdinalIgnoreCase));

        public IReadOnlyList<Resource> GetChildren(string resourceId) => [];

        public ResourceGroup? GetGroupForResource(string resourceId) => null;

        public bool IsRegistered(string resourceId) => GetResource(resourceId) is not null;
    }
}
