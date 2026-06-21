using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.Logs;

public sealed class LogSourceCatalog(
    IResourceManagerStore resourceManager,
    IReadOnlyList<ILogProvider> providers,
    IReadOnlyList<ILogSourceContributor> sourceContributors) : ILogSourceCatalog
{
    public IReadOnlyList<LogSource> GetLogSources()
    {
        var resources = resourceManager.GetResources();
        var visibleResourceIds = resources
            .Select(resource => resource.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var descriptors = providers
            .SelectMany(provider => provider.GetLogs())
            .Where(log => log.ResourceId is null || visibleResourceIds.Contains(log.ResourceId))
            .ToArray();
        var contributedSources = sourceContributors
            .SelectMany(contributor => contributor.GetLogSources())
            .Where(source => source.ResourceId is null || visibleResourceIds.Contains(source.ResourceId))
            .ToArray();
        var contributedSourceIds = contributedSources
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
                var contributedSource = descriptor is null
                    ? contributedSources.FirstOrDefault(contributedSource =>
                        IsMatchingSource(resource, source, contributedSource))
                    : contributedSources.FirstOrDefault(contributedSource =>
                        string.Equals(contributedSource.Id, descriptor.Id, StringComparison.OrdinalIgnoreCase));
                if (descriptor is not null)
                {
                    matchedSourceIds.Add(descriptor.Id);
                }
                if (contributedSource is not null)
                {
                    matchedSourceIds.Add(contributedSource.Id);
                }

                projected.Add(ToLogSource(resource, source, descriptor, contributedSource));
            }
        }

        projected.AddRange(contributedSources
            .Where(source => !matchedSourceIds.Contains(source.Id)));

        projected.AddRange(descriptors
            .Where(descriptor =>
                !matchedSourceIds.Contains(descriptor.Id) &&
                !contributedSourceIds.Contains(descriptor.Id))
            .Select(descriptor => descriptor.ToLogSource()));

        return projected
            .OrderBy(source => source.SourceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(source => source.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public LogSource? GetLogSource(string logSourceId) =>
        GetLogSources()
            .FirstOrDefault(source => string.Equals(source.Id, logSourceId, StringComparison.OrdinalIgnoreCase));

    private static LogSource ToLogSource(
        Resource resource,
        ResourceLogSource source,
        LogDescriptor? descriptor,
        LogSource? contributedSource)
    {
        var descriptorSource = descriptor?.ToLogSource();
        return new LogSource(
            descriptor?.Id ?? contributedSource?.Id ?? GetResourceLogSourceId(resource.Id, source.Id),
            source.Name,
            contributedSource?.Provider ?? descriptorSource?.Provider ?? resource.Provider,
            contributedSource?.SourceName ?? descriptorSource?.SourceName ?? resource.Name,
            contributedSource?.SourceKind ?? descriptorSource?.SourceKind ?? LogSourceKind.Resource,
            source.Kind,
            source.Format,
            source.Storage,
            descriptorSource is null && contributedSource is null
                ? source.Capabilities
                : source.Capabilities |
                    (descriptorSource?.Capabilities ?? LogSourceCapabilities.None) |
                    (contributedSource?.Capabilities ?? LogSourceCapabilities.None),
            resource.Id,
            contributedSource?.ArtifactId ?? descriptorSource?.ArtifactId,
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
        LogSource contributedSource) =>
        string.Equals(contributedSource.ResourceId, resource.Id, StringComparison.OrdinalIgnoreCase) &&
        contributedSource.Kind == source.Kind &&
        (string.Equals(contributedSource.Name, source.Name, StringComparison.OrdinalIgnoreCase) ||
            source.Purpose == ResourceLogSourcePurpose.Default);

    private static string GetResourceLogSourceId(string resourceId, string sourceId) =>
        $"{resourceId}:log-source:{sourceId}";
}
