using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.Providers;

public interface IRabbitMQAccessReconciler
{
    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileAccessAsync(
        Resource resource,
        IReadOnlyList<ResourcePermissionGrant> grants,
        CancellationToken cancellationToken = default);
}

public sealed class NoopRabbitMQAccessReconciler :
    IRabbitMQAccessReconciler
{
    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileAccessAsync(
        Resource resource,
        IReadOnlyList<ResourcePermissionGrant> grants,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>(
        [
            RabbitMQAccessReconcilerReadiness.CreateMissingDiagnostic(resource)
        ]);
}

internal static class RabbitMQAccessReconcilerReadiness
{
    public const string DiagnosticCode = "application.rabbitmq.accessReconcilerMissing";

    public static bool IsMissing(IRabbitMQAccessReconciler? accessReconciler) =>
        accessReconciler is null or NoopRabbitMQAccessReconciler;

    public static string CreateMissingReason(Resource resource) =>
        $"RabbitMQ resource '{resource.EffectiveResourceId}' cannot reconcile broker access because no RabbitMQ access reconciler is registered.";

    public static ResourceDefinitionDiagnostic CreateMissingDiagnostic(Resource resource) =>
        ResourceDefinitionDiagnostic.Error(
            DiagnosticCode,
            CreateMissingReason(resource),
            resource.EffectiveResourceId);
}
