namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public interface IContainerApplicationRuntimeHandler
{
    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
        Resource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyImageAsync(
        Resource resource,
        CancellationToken cancellationToken = default);
}

public sealed class NoopContainerApplicationRuntimeHandler :
    IContainerApplicationRuntimeHandler
{
    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
        Resource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);

    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyImageAsync(
        Resource resource,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
}
