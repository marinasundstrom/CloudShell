namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public enum ContainerApplicationRuntimeStatus
{
    Unknown,
    Stopped,
    Running
}

public interface IContainerApplicationRuntimeHandler
{
    ContainerApplicationRuntimeStatus GetStatus(Resource resource);

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
    public ContainerApplicationRuntimeStatus GetStatus(Resource resource) =>
        ContainerApplicationRuntimeStatus.Unknown;

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
