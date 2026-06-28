using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Providers.Applications;

internal sealed class ReplicatedContainerHealthGraphOnlyMonitoringProvider(
    IReplicatedContainerHealthCommandRunner commandRunner) : IResourceMonitoringProvider
{
    private const string ProviderDisplayName = "Replicated Container Health";

    public bool CanMonitor(Resource resource) =>
        IsGraphRuntimeReplica(resource);

    public async Task<ResourceMonitoringSnapshot?> GetMonitoringSnapshotAsync(
        Resource resource,
        CancellationToken cancellationToken = default)
    {
        if (!CanMonitor(resource))
        {
            return null;
        }

        var timestamp = DateTimeOffset.UtcNow;
        if (resource.State != ResourceState.Running)
        {
            return new ResourceMonitoringSnapshot(
                resource.Id,
                ProviderDisplayName,
                timestamp,
                [],
                "Unavailable",
                "Graph replica metrics are available only while the replica is running.");
        }

        var containerName = GetAttribute(resource, ResourceAttributeNames.RuntimeContainerName);
        if (string.IsNullOrWhiteSpace(containerName))
        {
            return new ResourceMonitoringSnapshot(
                resource.Id,
                ProviderDisplayName,
                timestamp,
                [],
                "Unavailable",
                "Graph replica metrics require a projected runtime container name.");
        }

        var result = await commandRunner.RunAsync(
            "docker",
            ["stats", "--no-stream", "--format", "{{json .}}", containerName],
            cancellationToken,
            throwOnError: false);
        if (result.ExitCode != 0 ||
            !ApplicationContainerMonitoringMetrics.TryParseStatsJson(
                result.Output,
                DateTimeOffset.UtcNow,
                out var snapshot))
        {
            return new ResourceMonitoringSnapshot(
                resource.Id,
                ProviderDisplayName,
                timestamp,
                [],
                "Unavailable",
                string.IsNullOrWhiteSpace(result.Error)
                    ? "The container runtime did not return a stats snapshot for the graph replica."
                    : result.Error.Trim());
        }

        return new ResourceMonitoringSnapshot(
            resource.Id,
            ProviderDisplayName,
            snapshot.Timestamp,
            ApplicationContainerMonitoringMetrics.CreateMetricSamples(snapshot),
            "Available",
            "Graph replica container runtime metrics.");
    }

    private static bool IsGraphRuntimeReplica(Resource resource) =>
        string.Equals(resource.EffectiveTypeId, "runtime.container", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(
            resource.OwnerResourceId,
            ReplicatedContainerHealthGraphOnlyRuntimeConventions.GraphApiResourceId,
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(
            GetAttribute(resource, ResourceAttributeNames.RuntimeKind),
            "containerReplica",
            StringComparison.OrdinalIgnoreCase);

    private static string GetAttribute(Resource resource, string name) =>
        resource.ResourceAttributes.TryGetValue(name, out var value)
            ? value
            : string.Empty;
}
