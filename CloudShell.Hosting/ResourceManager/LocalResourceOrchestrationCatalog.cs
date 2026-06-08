using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Hosting.ResourceManager;

internal sealed class LocalResourceOrchestrationCatalog : IResourceOrchestrationCatalog
{
    public Task<ResourceOrchestrationCatalogSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ResourceOrchestrationCatalogSnapshot.Empty);
}
