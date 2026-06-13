namespace CloudShell.Abstractions.ResourceManager;

public interface IResourceOrchestrationCatalog
{
    Task<ResourceOrchestrationCatalogSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default);
}

public sealed record ResourceOrchestrationCatalogSnapshot(
    IReadOnlyList<Resource> Resources,
    IReadOnlyDictionary<string, ResourceWorkloadConfiguration> Workloads,
    IReadOnlyDictionary<string, ContainerHostDescriptor> ContainerHosts)
{
    public static ResourceOrchestrationCatalogSnapshot Empty { get; } = new(
        [],
        new Dictionary<string, ResourceWorkloadConfiguration>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, ContainerHostDescriptor>(StringComparer.OrdinalIgnoreCase));
}
