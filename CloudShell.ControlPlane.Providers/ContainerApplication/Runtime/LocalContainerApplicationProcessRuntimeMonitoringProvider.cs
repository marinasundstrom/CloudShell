using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ResourceManager;
using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;

namespace CloudShell.ControlPlane.Providers;

public sealed class LocalContainerApplicationProcessRuntimeMonitoringProvider(
    LocalContainerApplicationProcessRuntimeBridge bridge) : IResourceMonitoringProvider
{
    private const string ProviderDisplayName = "Container app local process runtime";

    public bool CanMonitor(ResourceManagerResource resource) =>
        string.Equals(resource.EffectiveTypeId, "runtime.container", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(
            GetAttribute(resource, ResourceAttributeNames.RuntimeKind),
            "containerReplica",
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(
            GetAttribute(resource, ResourceAttributeNames.RuntimeMaterialization),
            "localProcess",
            StringComparison.OrdinalIgnoreCase);

    public async Task<ResourceMonitoringSnapshot?> GetMonitoringSnapshotAsync(
        ResourceManagerResource resource,
        CancellationToken cancellationToken = default)
    {
        if (!CanMonitor(resource))
        {
            return null;
        }

        var timestamp = DateTimeOffset.UtcNow;
        if (resource.State != CloudShell.Abstractions.ResourceManager.ResourceState.Running)
        {
            return new ResourceMonitoringSnapshot(
                resource.Id,
                ProviderDisplayName,
                timestamp,
                [],
                "Unavailable",
                "Runtime replica metrics are available only while the replica is running.");
        }

        var snapshot = await bridge.GetRuntimeReplicaMonitoringSnapshotAsync(
            resource.Id,
            cancellationToken);
        if (snapshot is null)
        {
            return new ResourceMonitoringSnapshot(
                resource.Id,
                ProviderDisplayName,
                timestamp,
                [],
                "Unavailable",
                "The local process runtime replica could not be observed.");
        }

        return new ResourceMonitoringSnapshot(
            resource.Id,
            ProviderDisplayName,
            snapshot.Timestamp,
            ResourceProcessMonitoringMetricSamples.Create(snapshot, "container app replica process"),
            "Available",
            "Container app local process replica metrics.");
    }

    private static string GetAttribute(
        ResourceManagerResource resource,
        string name) =>
        resource.ResourceAttributes.TryGetValue(name, out var value)
            ? value
            : string.Empty;
}
