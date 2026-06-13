namespace CloudShell.Abstractions.ResourceManager;

public interface IHostScopedResourceCleanupProvider
{
    Task CleanupHostScopedResourcesAsync(CancellationToken cancellationToken = default);
}
