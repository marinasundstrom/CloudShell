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
                    Location: "provider://diagnostics",
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
    public async Task ReadLogSourceAsync_UsesProjectedLogSourceSession()
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
                    Location: "provider://diagnostics",
                    Origin: ResourceLogSourceOrigin.ProviderProjected)
            ]);
        var store = new LogStore(
            [provider],
            new TestResourceManagerStore([]),
            new CloudShellExtensionRegistry(),
            new InMemoryCloudShellExtensionActivationStore());

        var entries = await store.ReadLogSourceAsync("provider:diagnostics");

        var entry = Assert.Single(entries);
        Assert.Equal(ProviderProjectedEntry.Message, entry.Message);
        Assert.NotNull(provider.OpenedSource);
        Assert.Equal("provider://diagnostics", provider.OpenedSource.Location);
    }

    [Fact]
    public async Task ReadLogSourceAsync_ResolvesProviderForResourceDeclaredSource()
    {
        var resource = CreateResource(
            "application:api",
            "api",
            logSources:
            [
                new ResourceLogSource(
                    "file",
                    "Application file",
                    ResourceLogSourceKind.File,
                    Location: "logs/api.log",
                    Origin: ResourceLogSourceOrigin.UserConfigured)
            ]);
        var provider = new ResourceDeclaredLogProvider();
        var store = new LogStore(
            [provider],
            new TestResourceManagerStore([resource]),
            new CloudShellExtensionRegistry(),
            new InMemoryCloudShellExtensionActivationStore());

        var entries = await store.ReadLogSourceAsync("application:api:log-source:file");

        Assert.Equal(ProviderProjectedEntry.Message, Assert.Single(entries).Message);
        Assert.NotNull(provider.OpenedSource);
        Assert.Equal("application:api", provider.OpenedSource.ResourceId);
        Assert.Equal(ResourceLogSourceKind.File, provider.OpenedSource.Kind);
        Assert.Equal("logs/api.log", provider.OpenedSource.Location);
    }

    [Fact]
    public async Task OpenLogSourceSessionAsync_ReturnsDisposableResolvedSession()
    {
        var resource = CreateResource(
            "application:api",
            "api",
            logSources:
            [
                new ResourceLogSource(
                    "file",
                    "Application file",
                    ResourceLogSourceKind.File,
                    Location: "logs/api.log",
                    Origin: ResourceLogSourceOrigin.UserConfigured)
            ]);
        var provider = new ResourceDeclaredLogProvider();
        var store = new LogStore(
            [provider],
            new TestResourceManagerStore([resource]),
            new CloudShellExtensionRegistry(),
            new InMemoryCloudShellExtensionActivationStore());

        var session = await store.OpenLogSourceSessionAsync("application:api:log-source:file");

        Assert.NotNull(session);
        Assert.Equal("application:api:log-source:file", session.SourceId);
        Assert.Equal(LogSourceSessionStatus.Active, session.Status);
        Assert.Equal(ProviderProjectedEntry.Message, Assert.Single(await session.ReadAsync()).Message);
        await session.DisposeAsync();
        Assert.Equal(LogSourceSessionStatus.Closed, session.Status);
    }

    [Fact]
    public async Task ResourceEventLogProvider_ExposesActivityAsProjectedLogSource()
    {
        var resource = CreateResource("application:api", "api");
        var events = new InMemoryResourceEventStore();
        var provider = new ResourceEventLogProvider(
            events,
            new TestResourceManagerStore([resource]));
        events.Append(new ResourceEvent(
            resource.Id,
            "resource.updated",
            "Resource was updated.",
            DateTimeOffset.Parse("2026-06-21T12:00:00Z"),
            "operator"));

        var source = Assert.Single(provider.GetLogSources());
        var descriptor = Assert.Single(provider.GetLogs());
        var entries = await provider.ReadLogAsync(source.Id);

        Assert.Equal(ResourceEventLogProvider.GetLogId(resource.Id), source.Id);
        Assert.Equal("Activity", source.Name);
        Assert.Equal(resource.Id, source.ResourceId);
        Assert.Equal(LogSourceKind.Resource, source.SourceKind);
        Assert.Equal(ResourceLogSourceKind.Activity, source.Kind);
        Assert.Equal(LogFormat.ResourceEvent, source.Format);
        Assert.True(source.Capabilities.HasFlag(LogSourceCapabilities.Read));
        Assert.True(source.Capabilities.HasFlag(LogSourceCapabilities.Query));
        Assert.True(source.Capabilities.HasFlag(LogSourceCapabilities.StructuredFields));
        Assert.Equal(ResourceLogSourceOrigin.ProviderProjected, source.Origin);
        Assert.Equal(source.Id, descriptor.Id);
        Assert.Equal(source.ResourceId, descriptor.ResourceId);
        Assert.Equal(source.Kind, descriptor.Kind);
        Assert.Equal(source.Format, descriptor.Format);
        var entry = Assert.Single(entries);
        Assert.Equal("event", entry.Source);
        Assert.Equal("resource.updated", entry.EventId);
        Assert.Equal("operator", entry.Attributes?["triggeredBy"]);
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

        public LogSource? OpenedSource { get; private set; }

        public IReadOnlyList<LogSource> GetLogSources() =>
            [.. logs.Select(log => log.ToLogSource()), .. sources];

        public ValueTask<ILogSourceSession?> OpenLogSourceAsync(
            LogSource source,
            CancellationToken cancellationToken = default)
        {
            OpenedSource = source;
            return ValueTask.FromResult<ILogSourceSession?>(
                sources.Any(candidate => string.Equals(candidate.Id, source.Id, StringComparison.OrdinalIgnoreCase))
                    ? new TestLogSourceSession(source.Id)
                    : null);
        }

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
        public string Id { get; } = Guid.NewGuid().ToString("N");

        public string SourceId => sourceId;

        public LogSourceSessionStatus Status { get; private set; } = LogSourceSessionStatus.Active;

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

        public ValueTask DisposeAsync()
        {
            Status = LogSourceSessionStatus.Closed;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ResourceDeclaredLogProvider : ILogProvider
    {
        public string Id => "resource-declared";

        public string DisplayName => "Resource Declared";

        public LogSource? OpenedSource { get; private set; }

        public IReadOnlyList<LogDescriptor> GetLogs() => [];

        public IReadOnlyList<LogSource> GetLogSources() => [];

        public bool CanOpenLogSource(LogSource source) =>
            string.Equals(source.ResourceId, "application:api", StringComparison.OrdinalIgnoreCase) &&
            source.Kind == ResourceLogSourceKind.File;

        public ValueTask<ILogSourceSession?> OpenLogSourceAsync(
            LogSource source,
            CancellationToken cancellationToken = default)
        {
            OpenedSource = source;
            return ValueTask.FromResult<ILogSourceSession?>(new TestLogSourceSession(source.Id));
        }

        public Task<IReadOnlyList<LogEntry>> ReadLogAsync(
            string logId,
            int maxEntries = 200,
            DateTimeOffset? before = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LogEntry>>([]);
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
