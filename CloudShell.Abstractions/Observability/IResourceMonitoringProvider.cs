using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Abstractions.Observability;

public interface IResourceMonitoringProvider
{
    bool CanMonitor(Resource resource);

    Task<ResourceMonitoringSnapshot?> GetMonitoringSnapshotAsync(
        Resource resource,
        CancellationToken cancellationToken = default);
}

public sealed record ResourceMonitoringSnapshot(
    string ResourceId,
    string Provider,
    DateTimeOffset Timestamp,
    IReadOnlyList<ResourceMetricSample> Metrics,
    string? Status = null,
    string? Message = null);

public sealed record ResourceMetricSample(
    string Name,
    double Value,
    string Unit,
    DateTimeOffset Timestamp,
    string? DisplayName = null,
    string? Description = null,
    IReadOnlyDictionary<string, string>? Attributes = null);
