using CloudShell.Abstractions.Observability;
using CloudShell.ControlPlane.ResourceModel;
using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;
using ResourceManagerState = CloudShell.Abstractions.ResourceManager.ResourceState;

namespace CloudShell.ControlPlane.Providers;

public sealed class EventBrokerResourceManagerStateProvider(
    IEventBrokerRuntimeController? runtimeController = null) :
    IResourceModelResourceManagerStateProvider
{
    private readonly IEventBrokerRuntimeController _runtimeController =
        runtimeController ?? new NoopEventBrokerRuntimeController();

    public ResourceManagerState? GetState(Resource resource)
    {
        if (resource.Type.TypeId != EventBrokerResourceTypeProvider.ResourceTypeId)
        {
            return null;
        }

        return _runtimeController.GetStatus(resource) switch
        {
            ResourceWebAppRuntimeStatus.Running => ResourceManagerState.Running,
            ResourceWebAppRuntimeStatus.Stopped => ResourceManagerState.Stopped,
            _ => ResourceManagerState.Unknown
        };
    }
}

public sealed class EventBrokerResourceMonitoringProvider(
    IEventBrokerRuntimeMonitor? runtimeMonitor = null) : IResourceMonitoringProvider
{
    private const string ProviderDisplayName = "Event Broker";
    private readonly IEventBrokerRuntimeMonitor _runtimeMonitor =
        runtimeMonitor ?? new NoopEventBrokerRuntimeController();

    public bool CanMonitor(ResourceManagerResource resource) =>
        string.Equals(
            resource.EffectiveTypeId,
            EventBrokerResourceTypeProvider.ResourceTypeId.ToString(),
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
        var snapshot = await _runtimeMonitor.GetMonitoringSnapshotAsync(
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
                "The Event Broker process could not be observed.");
        }

        return new ResourceMonitoringSnapshot(
            resource.Id,
            ProviderDisplayName,
            snapshot.Timestamp,
            ResourceProcessMonitoringMetricSamples.Create(snapshot, "Event Broker process"),
            "Available",
            "Event Broker process metrics.");
    }
}
