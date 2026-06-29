using CloudShell.Abstractions.Observability;
using CloudShell.ControlPlane.ResourceModel;
using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;
using ResourceManagerState = CloudShell.Abstractions.ResourceManager.ResourceState;

namespace CloudShell.ControlPlane.Providers;

public sealed class ConfigurationStoreResourceManagerStateProvider(
    IConfigurationStoreRuntimeController? runtimeController = null) :
    IResourceModelResourceManagerStateProvider
{
    private readonly IConfigurationStoreRuntimeController _runtimeController =
        runtimeController ?? new NoopConfigurationStoreRuntimeController();

    public ResourceManagerState? GetState(Resource resource)
    {
        if (resource.Type.TypeId != ConfigurationStoreResourceTypeProvider.ResourceTypeId)
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

public sealed class ConfigurationStoreResourceManagerMonitoringProvider(
    IConfigurationStoreRuntimeMonitor? runtimeMonitor = null) : IResourceMonitoringProvider
{
    private const string ProviderDisplayName = "Configuration Store";
    private readonly IConfigurationStoreRuntimeMonitor _runtimeMonitor =
        runtimeMonitor ?? new NoopConfigurationStoreRuntimeController();

    public bool CanMonitor(ResourceManagerResource resource) =>
        string.Equals(
            resource.EffectiveTypeId,
            ConfigurationStoreResourceTypeProvider.ResourceTypeId.ToString(),
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
                "The Configuration Store process could not be observed.");
        }

        return new ResourceMonitoringSnapshot(
            resource.Id,
            ProviderDisplayName,
            snapshot.Timestamp,
            ResourceProcessMonitoringMetricSamples.Create(snapshot, "Configuration Store process"),
            "Available",
            "Configuration Store process metrics.");
    }
}

public sealed class SecretsVaultResourceManagerStateProvider(
    ISecretsVaultRuntimeController? runtimeController = null) :
    IResourceModelResourceManagerStateProvider
{
    private readonly ISecretsVaultRuntimeController _runtimeController =
        runtimeController ?? new NoopSecretsVaultRuntimeController();

    public ResourceManagerState? GetState(Resource resource)
    {
        if (resource.Type.TypeId != SecretsVaultResourceTypeProvider.ResourceTypeId)
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

public sealed class SecretsVaultResourceManagerMonitoringProvider(
    ISecretsVaultRuntimeMonitor? runtimeMonitor = null) : IResourceMonitoringProvider
{
    private const string ProviderDisplayName = "Secrets Vault";
    private readonly ISecretsVaultRuntimeMonitor _runtimeMonitor =
        runtimeMonitor ?? new NoopSecretsVaultRuntimeController();

    public bool CanMonitor(ResourceManagerResource resource) =>
        string.Equals(
            resource.EffectiveTypeId,
            SecretsVaultResourceTypeProvider.ResourceTypeId.ToString(),
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
                "The Secrets Vault process could not be observed.");
        }

        return new ResourceMonitoringSnapshot(
            resource.Id,
            ProviderDisplayName,
            snapshot.Timestamp,
            ResourceProcessMonitoringMetricSamples.Create(snapshot, "Secrets Vault process"),
            "Available",
            "Secrets Vault process metrics.");
    }
}
