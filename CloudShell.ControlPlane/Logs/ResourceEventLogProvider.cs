using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.Logs;

public sealed class ResourceEventLogProvider(
    IResourceEventStore events,
    IResourceManagerStore resourceManager) : ILogProvider
{
    private const string LogIdSuffix = ":resource-events";

    public string Id => "resource-events";

    public string DisplayName => "Resource events";

    public IReadOnlyList<LogDescriptor> GetLogs() =>
        resourceManager
            .GetResources()
            .Select(resource => new LogDescriptor(
                GetLogId(resource.Id),
                "Resource events",
                DisplayName,
                resource.Name,
                LogSourceKind.Resource,
                ResourceId: resource.Id,
                Description: "Actor-attributed operational events recorded for this resource."))
            .ToArray();

    public Task<IReadOnlyList<LogEntry>> ReadLogAsync(
        string logId,
        int maxEntries = 200,
        DateTimeOffset? before = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryGetResourceId(logId, out var resourceId))
        {
            return Task.FromResult<IReadOnlyList<LogEntry>>([]);
        }

        return Task.FromResult<IReadOnlyList<LogEntry>>(
            events.GetEvents(new ResourceEventQuery(
                    ResourceId: resourceId,
                    Before: before,
                    MaxEvents: maxEntries))
                .OrderBy(resourceEvent => resourceEvent.Timestamp)
                .Select(ToLogEntry)
                .ToArray());
    }

    public static string GetLogId(string resourceId) => $"{resourceId}{LogIdSuffix}";

    private static bool TryGetResourceId(
        string logId,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? resourceId)
    {
        if (logId.EndsWith(LogIdSuffix, StringComparison.OrdinalIgnoreCase))
        {
            resourceId = logId[..^LogIdSuffix.Length];
            return true;
        }

        resourceId = null;
        return false;
    }

    private static LogEntry ToLogEntry(ResourceEvent resourceEvent)
    {
        var actor = string.IsNullOrWhiteSpace(resourceEvent.TriggeredBy)
            ? "unspecified"
            : resourceEvent.TriggeredBy.Trim();
        return new LogEntry(
            resourceEvent.Timestamp,
            $"{resourceEvent.EventType}: {resourceEvent.Message} Triggered by: {actor}.",
            resourceEvent.Level,
            "event");
    }
}
