using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.Logs;

public sealed class LogSourceCatalog(
    IResourceManagerStore resourceManager,
    IReadOnlyList<ILogSourceContributor> sourceContributors) : ILogSourceCatalog
{
    public IReadOnlyList<LogSource> GetLogSources()
    {
        var resources = resourceManager.GetResources();
        var visibleResourceIds = resources
            .Select(resource => resource.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var contributedSources = sourceContributors
            .SelectMany(contributor => contributor.GetLogSources())
            .Where(source => source.ResourceId is null || visibleResourceIds.Contains(source.ResourceId))
            .ToArray();
        var projected = new List<LogSource>();
        var matchedSourceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var resource in resources)
        {
            foreach (var source in resource.ResourceLogSources)
            {
                var contributedSource = contributedSources.FirstOrDefault(contributedSource =>
                    IsMatchingSource(resource, source, contributedSource));
                if (contributedSource is not null)
                {
                    matchedSourceIds.Add(contributedSource.Id);
                }

                projected.Add(ToLogSource(resource, source, contributedSource));
            }
        }

        projected.AddRange(contributedSources
            .Where(source => !matchedSourceIds.Contains(source.Id)));

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
        LogSource? contributedSource)
    {
        return new LogSource(
            contributedSource?.Id ?? GetResourceLogSourceId(resource.Id, source.Id),
            source.Name,
            contributedSource?.Provider ?? resource.Provider,
            contributedSource?.SourceName ?? resource.Name,
            contributedSource?.SourceKind ?? LogSourceKind.Resource,
            source.Kind,
            source.Format,
            source.Storage,
            contributedSource is null
                ? source.Capabilities
                : source.Capabilities | contributedSource.Capabilities,
            resource.Id,
            contributedSource?.ArtifactId,
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
        LogSource contributedSource) =>
        string.Equals(contributedSource.ResourceId, resource.Id, StringComparison.OrdinalIgnoreCase) &&
        contributedSource.Kind == source.Kind &&
        (string.Equals(contributedSource.Name, source.Name, StringComparison.OrdinalIgnoreCase) ||
            source.Purpose == ResourceLogSourcePurpose.Default);

    private static string GetResourceLogSourceId(string resourceId, string sourceId) =>
        $"{resourceId}:log-source:{sourceId}";
}
