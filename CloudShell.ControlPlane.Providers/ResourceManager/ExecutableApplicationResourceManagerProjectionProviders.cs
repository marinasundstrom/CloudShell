using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ResourceManager;
using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;

namespace CloudShell.ControlPlane.Providers;

public sealed class ExecutableApplicationResourceManagerMonitoringProvider(
    IExecutableApplicationRuntimeMonitor? runtimeMonitor = null) : IResourceMonitoringProvider
{
    private const string ProviderDisplayName = "Executable application";
    private readonly IExecutableApplicationRuntimeMonitor _runtimeMonitor =
        runtimeMonitor ?? new NoopExecutableApplicationRuntimeController();

    public bool CanMonitor(ResourceManagerResource resource) =>
        string.Equals(
            resource.EffectiveTypeId,
            ExecutableApplicationResourceTypeProvider.ResourceTypeId.ToString(),
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
                "The executable application process could not be observed.");
        }

        return new ResourceMonitoringSnapshot(
            resource.Id,
            ProviderDisplayName,
            snapshot.Timestamp,
            ResourceProcessMonitoringMetricSamples.Create(
                snapshot,
                "Executable application process"),
            "Available",
            "Executable application process metrics.");
    }
}
