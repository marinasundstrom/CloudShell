namespace CloudShell.ControlPlane.Providers;

public enum RabbitMQRuntimeStatus
{
    Unknown,
    Stopped,
    Running
}

public interface IRabbitMQRuntimeHandler
{
    RabbitMQRuntimeStatus GetStatus(Resource resource);

    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
        Resource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default);
}

public sealed class NoopRabbitMQRuntimeHandler :
    IRabbitMQRuntimeHandler
{
    public RabbitMQRuntimeStatus GetStatus(Resource resource) =>
        RabbitMQRuntimeStatus.Unknown;

    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
        Resource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
}
