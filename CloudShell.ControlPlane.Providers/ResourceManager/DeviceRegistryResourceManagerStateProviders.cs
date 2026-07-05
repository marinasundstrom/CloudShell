using CloudShell.Abstractions.Observability;
using CloudShell.ControlPlane.ResourceModel;
using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;
using ResourceManagerState = CloudShell.Abstractions.ResourceManager.ResourceState;

namespace CloudShell.ControlPlane.Providers;

public sealed class DeviceRegistryResourceManagerStateProvider(
    IDeviceRegistryRuntimeController? runtimeController = null) :
    IResourceModelResourceManagerStateProvider
{
    private readonly IDeviceRegistryRuntimeController _runtimeController =
        runtimeController ?? new NoopDeviceRegistryRuntimeController();

    public ResourceManagerState? GetState(Resource resource)
    {
        if (resource.Type.TypeId != DeviceRegistryResourceTypeProvider.ResourceTypeId)
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

public sealed class DeviceRegistryResourceManagerMonitoringProvider(
    IDeviceRegistryRuntimeMonitor? runtimeMonitor = null) : IResourceMonitoringProvider
{
    private const string ProviderDisplayName = "Device Registry";
    private readonly IDeviceRegistryRuntimeMonitor _runtimeMonitor =
        runtimeMonitor ?? new NoopDeviceRegistryRuntimeController();

    public bool CanMonitor(ResourceManagerResource resource) =>
        string.Equals(
            resource.EffectiveTypeId,
            DeviceRegistryResourceTypeProvider.ResourceTypeId.ToString(),
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
        var snapshot = await _runtimeMonitor.GetMonitoringSnapshotAsync(resource.Id, cancellationToken);
        if (snapshot is null)
        {
            return new ResourceMonitoringSnapshot(
                resource.Id,
                ProviderDisplayName,
                timestamp,
                [],
                "Unavailable",
                "The Device Registry process could not be observed.");
        }

        return new ResourceMonitoringSnapshot(
            resource.Id,
            ProviderDisplayName,
            snapshot.Timestamp,
            ResourceProcessMonitoringMetricSamples.Create(snapshot, "Device Registry process"),
            "Available",
            "Device Registry process metrics.");
    }
}
