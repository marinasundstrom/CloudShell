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

    public IReadOnlyList<LogSource> GetLogSources()
    {
        var resources = resourceManager.GetResources();
        var visibleResourceIds = resources
            .Select(resource => resource.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var descriptors = Providers
            .SelectMany(provider => provider.GetLogs())
            .Where(log => log.ResourceId is null || visibleResourceIds.Contains(log.ResourceId))
            .ToArray();
        var providerSources = Providers
            .SelectMany(provider => provider.GetLogSources())
            .Where(source => source.ResourceId is null || visibleResourceIds.Contains(source.ResourceId))
            .ToArray();
        var providerSourceIds = providerSources
            .Select(source => source.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var projected = new List<LogSource>();
        var matchedSourceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var resource in resources)
        {
            foreach (var source in resource.ResourceLogSources)
            {
                var descriptor = descriptors.FirstOrDefault(descriptor =>
                    IsMatchingSource(resource, source, descriptor));
                var providerSource = descriptor is null
                    ? providerSources.FirstOrDefault(providerSource =>
                        IsMatchingSource(resource, source, providerSource))
                    : providerSources.FirstOrDefault(providerSource =>
                        string.Equals(providerSource.Id, descriptor.Id, StringComparison.OrdinalIgnoreCase));
                if (descriptor is not null)
                {
                    matchedSourceIds.Add(descriptor.Id);
                }
                if (providerSource is not null)
                {
                    matchedSourceIds.Add(providerSource.Id);
                }

                projected.Add(ToLogSource(resource, source, descriptor, providerSource));
            }
        }

        projected.AddRange(providerSources
            .Where(source => !matchedSourceIds.Contains(source.Id)));

        projected.AddRange(descriptors
            .Where(descriptor =>
                !matchedSourceIds.Contains(descriptor.Id) &&
                !providerSourceIds.Contains(descriptor.Id))
            .Select(descriptor => descriptor.ToLogSource()));

        return projected
            .OrderBy(source => source.SourceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(source => source.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<LogDescriptor> GetLogsForResource(string resourceId) =>
        GetLogs()
            .Where(log => string.Equals(log.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase))
            .ToArray();

    public LogDescriptor? GetLog(string logId) =>
        GetLogs().FirstOrDefault(log => string.Equals(log.Id, logId, StringComparison.OrdinalIgnoreCase));

    public LogSource? GetLogSource(string logSourceId) =>
        GetLogSources()
            .FirstOrDefault(source => string.Equals(source.Id, logSourceId, StringComparison.OrdinalIgnoreCase));

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
        foreach (var provider in Providers)
        {
            await using var session = await OpenProviderLogSourceAsync(provider, logSourceId, cancellationToken);
            if (session is not null)
            {
                return await session.ReadAsync(maxEntries, before, cancellationToken);
            }
        }

        return [];
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
        foreach (var provider in Providers)
        {
            var source = GetProviderLogSource(provider, logSourceId);
            if (source is null)
            {
                continue;
            }

            if (!source.SupportsStreaming)
            {
                yield break;
            }

            await using var session = await provider.OpenLogSourceAsync(source, cancellationToken);
            if (session is null)
            {
                yield break;
            }

            await foreach (var entry in session.StreamAsync(initialEntries, cancellationToken))
            {
                yield return entry;
            }

            yield break;
        }
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

    private static async ValueTask<ILogSourceSession?> OpenProviderLogSourceAsync(
        ILogProvider provider,
        string logSourceId,
        CancellationToken cancellationToken)
    {
        var source = GetProviderLogSource(provider, logSourceId);
        if (source is null)
        {
            return null;
        }

        return await provider.OpenLogSourceAsync(source, cancellationToken);
    }

    private static LogSource? GetProviderLogSource(
        ILogProvider provider,
        string logSourceId) =>
        provider.GetLogSources().FirstOrDefault(source =>
            string.Equals(source.Id, logSourceId, StringComparison.OrdinalIgnoreCase));

    private static LogSource ToLogSource(
        Resource resource,
        ResourceLogSource source,
        LogDescriptor? descriptor,
        LogSource? providerSource)
    {
        var descriptorSource = descriptor?.ToLogSource();
        return new LogSource(
            descriptor?.Id ?? providerSource?.Id ?? GetResourceLogSourceId(resource.Id, source.Id),
            source.Name,
            providerSource?.Provider ?? descriptorSource?.Provider ?? resource.Provider,
            providerSource?.SourceName ?? descriptorSource?.SourceName ?? resource.Name,
            providerSource?.SourceKind ?? descriptorSource?.SourceKind ?? LogSourceKind.Resource,
            source.Kind,
            source.Format,
            source.Storage,
            descriptorSource is null && providerSource is null
                ? source.Capabilities
                : source.Capabilities |
                    (descriptorSource?.Capabilities ?? LogSourceCapabilities.None) |
                    (providerSource?.Capabilities ?? LogSourceCapabilities.None),
            resource.Id,
            providerSource?.ArtifactId ?? descriptorSource?.ArtifactId,
            source.Location,
            source.ProducerResourceId,
            source.Description,
            source.Origin,
            source.Configuration,
            source.Purpose,
            source.Availability);
    }

    private static bool IsMatchingSource(
        Resource resource,
        ResourceLogSource source,
        LogDescriptor descriptor) =>
        string.Equals(descriptor.ResourceId, resource.Id, StringComparison.OrdinalIgnoreCase) &&
        descriptor.Kind == source.Kind &&
        (string.Equals(descriptor.Name, source.Name, StringComparison.OrdinalIgnoreCase) ||
            source.Purpose == ResourceLogSourcePurpose.Default);

    private static bool IsMatchingSource(
        Resource resource,
        ResourceLogSource source,
        LogSource providerSource) =>
        string.Equals(providerSource.ResourceId, resource.Id, StringComparison.OrdinalIgnoreCase) &&
        providerSource.Kind == source.Kind &&
        (string.Equals(providerSource.Name, source.Name, StringComparison.OrdinalIgnoreCase) ||
            source.Purpose == ResourceLogSourcePurpose.Default);

    private static string GetResourceLogSourceId(string resourceId, string sourceId) =>
        $"{resourceId}:log-source:{sourceId}";
}
