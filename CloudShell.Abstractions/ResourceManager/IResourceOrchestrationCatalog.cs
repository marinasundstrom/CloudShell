namespace CloudShell.Abstractions.ResourceManager;

public interface IResourceOrchestrationCatalog
{
    Task<ResourceOrchestrationCatalogSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default);
}

public sealed record ResourceOrchestrationCatalogSnapshot(
    IReadOnlyList<CloudResource> Resources,
    IReadOnlyDictionary<string, ResourceWorkloadConfiguration> Workloads,
    IReadOnlyDictionary<string, ContainerEngineResourceDefinition> ContainerEngines)
{
    public static ResourceOrchestrationCatalogSnapshot Empty { get; } = new(
        [],
        new Dictionary<string, ResourceWorkloadConfiguration>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, ContainerEngineResourceDefinition>(StringComparer.OrdinalIgnoreCase));
}
