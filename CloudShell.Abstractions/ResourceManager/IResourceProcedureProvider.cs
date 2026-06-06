namespace CloudShell.Abstractions.ResourceManager;

public interface IResourceProcedureProvider
{
    Task<ResourceProcedureResult> DeleteAsync(
        ResourceProcedureContext context,
        CancellationToken cancellationToken = default);
}
