namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public enum DockerContainerRuntimeStatus
{
    Unknown,
    Stopped,
    Running,
    Paused
}

public interface IDockerContainerRuntimeHandler
{
    DockerContainerRuntimeStatus GetStatus(Resource resource);

    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
        Resource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default);
}

public sealed class NoopDockerContainerRuntimeHandler :
    IDockerContainerRuntimeHandler
{
    public DockerContainerRuntimeStatus GetStatus(Resource resource) =>
        DockerContainerRuntimeStatus.Unknown;

    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
        Resource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
}
