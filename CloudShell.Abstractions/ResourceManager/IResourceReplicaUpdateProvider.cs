namespace CloudShell.Abstractions.ResourceManager;

public interface IResourceReplicaUpdateProvider
{
    bool CanUpdateReplicas(Resource resource);

    Task<ResourceProcedureResult> UpdateReplicasAsync(
        ResourceProcedureContext context,
        int replicas,
        bool restartIfRunning,
        string? triggeredBy = null,
        CancellationToken cancellationToken = default);
}
