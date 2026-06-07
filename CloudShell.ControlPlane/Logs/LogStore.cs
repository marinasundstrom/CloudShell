using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.Logs;

public sealed class LogStore(
    IEnumerable<ILogProvider> providers,
    IResourceManagerStore resourceManager,
    CloudShellExtensionRegistry extensionRegistry,
    ICloudShellExtensionActivationStore activationStore) : ILogStore
{
    private readonly IReadOnlyList<ILogProvider> providers = providers.ToArray();

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

    public IReadOnlyList<LogDescriptor> GetLogsForResource(string resourceId) =>
        GetLogs()
            .Where(log => string.Equals(log.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase))
            .ToArray();

    public LogDescriptor? GetLog(string logId) =>
        GetLogs().FirstOrDefault(log => string.Equals(log.Id, logId, StringComparison.OrdinalIgnoreCase));

    public async Task<IReadOnlyList<LogEntry>> ReadLogAsync(
        string logId,
        int maxEntries = 200,
        DateTimeOffset? before = null,
        CancellationToken cancellationToken = default)
    {
        foreach (var provider in Providers)
        {
            if (provider.GetLogs().Any(log => string.Equals(log.Id, logId, StringComparison.OrdinalIgnoreCase)))
            {
                return await provider.ReadLogAsync(logId, maxEntries, before, cancellationToken);
            }
        }

        return [];
    }

    public async IAsyncEnumerable<LogEntry> StreamLogAsync(
        string logId,
        int initialEntries = 50,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var provider in Providers)
        {
            var log = provider.GetLogs().FirstOrDefault(log =>
                string.Equals(log.Id, logId, StringComparison.OrdinalIgnoreCase));
            if (log is null)
            {
                continue;
            }

            if (!log.SupportsStreaming)
            {
                yield break;
            }

            await foreach (var entry in provider.StreamLogAsync(logId, initialEntries, cancellationToken))
            {
                yield return entry;
            }

            yield break;
        }
    }

    private bool IsProviderActive(ILogProvider provider)
    {
        var activeProviderTypes = extensionRegistry
            .GetActiveExtensions(activationStore)
            .SelectMany(extension => extension.LogProviderTypes)
            .ToHashSet();

        var providerType = provider.GetType();
        return activeProviderTypes.Any(type => type.IsAssignableFrom(providerType));
    }
}
