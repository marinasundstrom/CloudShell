namespace CloudShell.Abstractions.ResourceManager;

public interface IResourceProcedureProvider
{
    Task<ResourceProcedureResult> DeleteAsync(
        ResourceProcedureContext context,
        CancellationToken cancellationToken = default);

    Task<ResourceProcedureResult> ExecuteActionAsync(
        ResourceProcedureContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            $"The resource action '{action.DisplayName}' is not supported by this provider.");
}
