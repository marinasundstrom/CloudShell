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
            new ResourceDefinitionDiagnostic(
                ResourceDefinitionDiagnosticSeverity.Information,
                "application.rabbitmq.reconcileAccessPending",
                $"RabbitMQ access reconciliation is registered for {grants.Count} grant(s), but no provider has applied CloudShell grants to broker-native users yet.",
                resource.EffectiveResourceId)
        ]);
}
