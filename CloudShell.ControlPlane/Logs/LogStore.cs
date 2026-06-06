using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.Logs;

public sealed class LogStore(
    IEnumerable<ILogProvider> providers,
    IResourceManagerStore resourceManager) : ILogStore
{
    public IReadOnlyList<ILogProvider> Providers { get; } = providers
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
        CancellationToken cancellationToken = default)
    {
        foreach (var provider in Providers)
        {
            if (provider.GetLogs().Any(log => string.Equals(log.Id, logId, StringComparison.OrdinalIgnoreCase)))
            {
                return await provider.ReadLogAsync(logId, maxEntries, cancellationToken);
            }
        }

        return [];
    }
}
