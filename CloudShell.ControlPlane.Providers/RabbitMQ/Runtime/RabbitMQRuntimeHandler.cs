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
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>(
        [
            RabbitMQRuntimeReadiness.CreateMissingDiagnostic(resource, operationId)
        ]);
}

internal static class RabbitMQRuntimeReadiness
{
    public const string DiagnosticCode = "application.rabbitmq.runtimeHandlerMissing";

    public static bool IsMissing(IRabbitMQRuntimeHandler? runtimeHandler) =>
        runtimeHandler is null or NoopRabbitMQRuntimeHandler;

    public static string CreateMissingReason(Resource resource, ResourceOperationId operationId) =>
        $"RabbitMQ resource '{resource.EffectiveResourceId}' cannot execute '{operationId}' because no RabbitMQ runtime handler is registered.";

    public static ResourceDefinitionDiagnostic CreateMissingDiagnostic(
        Resource resource,
        ResourceOperationId operationId) =>
        ResourceDefinitionDiagnostic.Error(
            DiagnosticCode,
            CreateMissingReason(resource, operationId),
            resource.EffectiveResourceId);
}
