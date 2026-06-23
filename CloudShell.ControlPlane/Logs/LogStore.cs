using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.Logs;

public sealed class LogStore(
    IEnumerable<ILogProvider> providers,
    IResourceManagerStore resourceManager,
    CloudShellExtensionRegistry extensionRegistry,
    ICloudShellExtensionActivationStore activationStore,
    IEnumerable<ILogSourceContributor>? sourceContributors = null) : ILogStore
{
    private readonly IReadOnlyList<ILogProvider> providers = providers.ToArray();
    private readonly IReadOnlyList<ILogSourceContributor> sourceContributors =
        sourceContributors?.ToArray() ?? [];

    public IReadOnlyList<ILogProvider> Providers => providers
        .Where(IsProviderActive)
        .OrderBy(provider => provider.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public IReadOnlyList<LogDescriptor> GetLogs()
    {
        var visibleResourceIds = resourceManager.GetResources()
            .Select(resource => resource.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return Providers
            .SelectMany(provider => provider.GetLogs())
            .Where(log => log.ResourceId is null || visibleResourceIds.Contains(log.ResourceId))
            .OrderBy(log => log.SourceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(log => log.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<LogSource> GetLogSources()
        => CreateSourceCatalog().GetLogSources();

    public IReadOnlyList<LogDescriptor> GetLogsForResource(string resourceId) =>
        GetLogs()
            .Where(log => string.Equals(log.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase))
            .ToArray();

    public LogDescriptor? GetLog(string logId) =>
        GetLogs().FirstOrDefault(log => string.Equals(log.Id, logId, StringComparison.OrdinalIgnoreCase));

    public LogSource? GetLogSource(string logSourceId) =>
        CreateSourceCatalog().GetLogSource(logSourceId);

    public async Task<IReadOnlyList<LogEntry>> ReadLogAsync(
        string logId,
        int maxEntries = 200,
        DateTimeOffset? before = null,
        CancellationToken cancellationToken = default) =>
        await ReadLogSourceAsync(logId, maxEntries, before, cancellationToken);

    public async Task<IReadOnlyList<LogEntry>> ReadLogSourceAsync(
        string logSourceId,
        int maxEntries = 200,
        DateTimeOffset? before = null,
        CancellationToken cancellationToken = default)
    {
        var source = GetLogSource(logSourceId);
        if (source is null || !source.Capabilities.HasFlag(LogSourceCapabilities.Read))
        {
            return [];
        }

        await using var session = await OpenLogSourceSessionAsync(logSourceId, cancellationToken);
        return session is null
            ? []
            : await session.ReadAsync(maxEntries, before, cancellationToken);
    }

    public async IAsyncEnumerable<LogEntry> StreamLogAsync(
        string logId,
        int initialEntries = 50,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var entry in StreamLogSourceAsync(logId, initialEntries, cancellationToken))
        {
            yield return entry;
        }
    }

    public async IAsyncEnumerable<LogEntry> StreamLogSourceAsync(
        string logSourceId,
        int initialEntries = 50,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var source = GetLogSource(logSourceId);
        if (source is null || !source.SupportsStreaming)
        {
            yield break;
        }

        await using var session = await OpenLogSourceSessionAsync(logSourceId, cancellationToken);
        if (session is null)
        {
            yield break;
        }

        await foreach (var entry in session.StreamAsync(initialEntries, cancellationToken))
        {
            yield return entry;
        }
    }

    public async ValueTask<ILogSourceSession?> OpenLogSourceSessionAsync(
        string logSourceId,
        CancellationToken cancellationToken = default)
    {
        var source = GetLogSource(logSourceId);
        if (source is null)
        {
            return null;
        }

        foreach (var provider in Providers)
        {
            if (!provider.CanOpenLogSource(source))
            {
                continue;
            }

            var session = await provider.OpenLogSourceAsync(source, cancellationToken);
            if (session is not null)
            {
                return session;
            }
        }

        return null;
    }

    private bool IsProviderActive(ILogProvider provider)
    {
        var extensionProviderTypes = extensionRegistry
            .Extensions
            .SelectMany(extension => extension.LogProviderTypes)
            .ToArray();
        var providerType = provider.GetType();
        var isExtensionProvider = extensionProviderTypes.Any(type => type.IsAssignableFrom(providerType));
        if (!isExtensionProvider)
        {
            return true;
        }

        return extensionRegistry
            .GetActiveExtensions(activationStore)
            .SelectMany(extension => extension.LogProviderTypes)
            .Any(type => type.IsAssignableFrom(providerType));
    }

    private ILogSourceCatalog CreateSourceCatalog() =>
        new LogSourceCatalog(resourceManager, GetSourceContributors());

    private IReadOnlyList<ILogSourceContributor> GetSourceContributors()
    {
        var activeProviders = Providers;
        return activeProviders
            .Cast<ILogSourceContributor>()
            .Concat(sourceContributors
                .Where(IsSourceContributorActive)
                .Where(contributor =>
                    activeProviders.All(provider => !ReferenceEquals(provider, contributor))))
            .ToArray();
    }

    private bool IsSourceContributorActive(ILogSourceContributor contributor)
    {
        var extensionContributorTypes = extensionRegistry
            .Extensions
            .SelectMany(extension => extension.LogSourceContributorTypes)
            .ToArray();
        var contributorType = contributor.GetType();
        var isExtensionContributor = extensionContributorTypes.Any(type => type.IsAssignableFrom(contributorType));
        if (!isExtensionContributor)
        {
            return true;
        }

        return extensionRegistry
            .GetActiveExtensions(activationStore)
            .SelectMany(extension => extension.LogSourceContributorTypes)
            .Any(type => type.IsAssignableFrom(contributorType));
    }
}
