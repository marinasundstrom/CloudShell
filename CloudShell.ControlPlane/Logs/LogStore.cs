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
        var projected = new List<LogSource>();
        var matchedDescriptors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var resource in resources)
        {
            foreach (var source in resource.ResourceLogSources)
            {
                var descriptor = descriptors.FirstOrDefault(descriptor =>
                    IsMatchingSource(resource, source, descriptor));
                if (descriptor is not null)
                {
                    matchedDescriptors.Add(descriptor.Id);
                }

                projected.Add(ToLogSource(resource, source, descriptor));
            }
        }

        projected.AddRange(descriptors
            .Where(descriptor => !matchedDescriptors.Contains(descriptor.Id))
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

    private static LogSource ToLogSource(
        Resource resource,
        ResourceLogSource source,
        LogDescriptor? descriptor)
    {
        var descriptorSource = descriptor?.ToLogSource();
        return new LogSource(
            descriptor?.Id ?? GetResourceLogSourceId(resource.Id, source.Id),
            source.Name,
            descriptorSource?.Provider ?? resource.Provider,
            descriptorSource?.SourceName ?? resource.Name,
            descriptorSource?.SourceKind ?? LogSourceKind.Resource,
            source.Kind,
            source.Format,
            source.Storage,
            descriptorSource is null
                ? source.Capabilities
                : source.Capabilities | descriptorSource.Capabilities,
            resource.Id,
            descriptorSource?.ArtifactId,
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

    private static string GetResourceLogSourceId(string resourceId, string sourceId) =>
        $"{resourceId}:log-source:{sourceId}";
}
