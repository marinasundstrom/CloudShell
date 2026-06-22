namespace CloudShell.Abstractions.ResourceManager;

public interface IResourceImageUpdateProvider
{
    bool CanUpdateImage(Resource resource);

    Task<ResourceProcedureResult> UpdateImageAsync(
        ResourceProcedureContext context,
        string image,
        bool restartIfRunning,
        string? triggeredBy = null,
        CancellationToken cancellationToken = default,
        int? requestedReplicas = null);
}
