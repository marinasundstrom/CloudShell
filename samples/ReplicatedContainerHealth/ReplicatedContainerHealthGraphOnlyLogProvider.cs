using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using System.Globalization;

internal sealed class ReplicatedContainerHealthGraphOnlyLogProvider(
    IResourceManagerStore resourceManager) : ILogProvider
{
    private const string GraphApiResourceId = "application.container-app:graph-api";
    private const string ContainerReplicasAttribute = "container.replicas";

    public string Id => "replicated-container-health.graph-only";

    public string DisplayName => "Replicated Container Health";

    public IReadOnlyList<LogSource> GetLogSources()
    {
        var resource = resourceManager.GetResource(GraphApiResourceId);
        if (resource is null)
        {
            return [];
        }

        var replicas = ResolveReplicas(resource);
        return Enumerable
            .Range(1, replicas)
            .Select(replica => new LogSource(
                GetLogSourceId(replica),
                $"Replica {replica.ToString(CultureInfo.InvariantCulture)} logs",
                DisplayName,
                resource.Name,
                LogSourceKind.Resource,
                Kind: ResourceLogSourceKind.Container,
                Format: LogFormat.JsonConsole,
                Capabilities: LogSourceCapabilities.Read,
                ResourceId: GraphApiResourceId,
                ProducerResourceId: GraphApiResourceId,
                Description: "Graph-only replica container logs.",
                Origin: ResourceLogSourceOrigin.ProviderProjected,
                Purpose: ResourceLogSourcePurpose.Default,
                Availability: LogSourceAvailability.ProducerRunning))
            .ToArray();
    }

    public bool CanOpenLogSource(LogSource source) =>
        string.Equals(source.Provider, DisplayName, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(source.ResourceId, GraphApiResourceId, StringComparison.OrdinalIgnoreCase) &&
        TryGetReplicaFromLogSourceId(source.Id, out _);

    public Task<IReadOnlyList<LogEntry>> ReadLogSourceAsync(
        string logSourceId,
        int maxEntries = 200,
        DateTimeOffset? before = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<LogEntry>>([]);
    }

    internal static string GetLogSourceId(int replica) =>
        $"{GraphApiResourceId}:replica-{replica.ToString(CultureInfo.InvariantCulture)}:logs";

    private static int ResolveReplicas(Resource resource) =>
        resource.ResourceAttributes.TryGetValue(ContainerReplicasAttribute, out var value) &&
        int.TryParse(value, out var replicas)
            ? Math.Max(1, replicas)
            : 1;

    private static bool TryGetReplicaFromLogSourceId(
        string logSourceId,
        out int replica)
    {
        replica = 0;
        var prefix = $"{GraphApiResourceId}:replica-";
        const string suffix = ":logs";
        if (!logSourceId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !logSourceId.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var value = logSourceId[prefix.Length..^suffix.Length];
        return int.TryParse(value, out replica) && replica > 0;
    }
}
