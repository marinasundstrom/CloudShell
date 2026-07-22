using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.Providers;

public interface IContainerApplicationRuntimeTarget :
    IContainerApplicationRuntimeHandler,
    IContainerApplicationOrchestratorRuntimeHandler
{
    bool CanHandle(Resource resource);
}

public sealed class DelegatingContainerApplicationRuntimeHandler(
    IEnumerable<IContainerApplicationRuntimeTarget> targets) :
    IContainerApplicationRuntimeHandler,
    IContainerApplicationOrchestratorRuntimeHandler,
    IContainerApplicationRuntimeReadinessProvider
{
    private readonly NoopContainerApplicationRuntimeHandler fallback = new();
    private readonly IReadOnlyList<IContainerApplicationRuntimeTarget> targets = targets.ToArray();

    public ContainerApplicationRuntimeStatus GetStatus(Resource resource) =>
        TryGetTarget(resource, out var target)
            ? target.GetStatus(resource)
            : ContainerApplicationRuntimeStatus.Unknown;

    public string? GetOperationUnavailableReason(
        Resource resource,
        ResourceOperationId operationId)
    {
        if (!TryGetTarget(resource, out var target))
        {
            return NoopContainerApplicationRuntimeHandler.CreateRuntimeUnavailableReason(
                resource,
                operationId);
        }

        return target is IContainerApplicationRuntimeReadinessProvider readinessProvider
            ? readinessProvider.GetOperationUnavailableReason(resource, operationId)
            : null;
    }

    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
        Resource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default) =>
        TryGetTarget(resource, out var target)
            ? target.ExecuteLifecycleAsync(resource, operationId, cancellationToken)
            : fallback.ExecuteLifecycleAsync(resource, operationId, cancellationToken);

    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyImageAsync(
        Resource resource,
        CancellationToken cancellationToken = default) =>
        TryGetTarget(resource, out var target)
            ? target.ApplyImageAsync(resource, cancellationToken)
            : fallback.ApplyImageAsync(resource, cancellationToken);

    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyReplicasAsync(
        Resource resource,
        CancellationToken cancellationToken = default) =>
        TryGetTarget(resource, out var target)
            ? target.ApplyReplicasAsync(resource, cancellationToken)
            : fallback.ApplyReplicasAsync(resource, cancellationToken);

    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> PrepareOrchestratorServiceAsync(
        Resource resource,
        ResourceOrchestratorService service,
        ResourceOrchestratorReplicaGroup? replicaGroup,
        IReadOnlyList<ResourceOrchestratorServiceRoutingBindingDefinition> routingBindings,
        CancellationToken cancellationToken = default) =>
        TryGetTarget(resource, out var target)
            ? target.PrepareOrchestratorServiceAsync(resource, service, replicaGroup, routingBindings, cancellationToken)
            : EmptyAsync();

    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileOrchestratorServiceRoutingAsync(
        Resource resource,
        ResourceOrchestratorService service,
        ResourceOrchestratorReplicaGroup? replicaGroup,
        IReadOnlyList<ResourceOrchestratorServiceRoutingBindingDefinition> routingBindings,
        CancellationToken cancellationToken = default) =>
        TryGetTarget(resource, out var target)
            ? target.ReconcileOrchestratorServiceRoutingAsync(resource, service, replicaGroup, routingBindings, cancellationToken)
            : EmptyAsync();

    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> TearDownOrchestratorServiceRoutingAsync(
        Resource resource,
        ResourceOrchestratorService service,
        ResourceOrchestratorReplicaGroup? replicaGroup,
        IReadOnlyList<ResourceOrchestratorServiceRoutingBindingDefinition> routingBindings,
        CancellationToken cancellationToken = default) =>
        TryGetTarget(resource, out var target)
            ? target.TearDownOrchestratorServiceRoutingAsync(resource, service, replicaGroup, routingBindings, cancellationToken)
            : EmptyAsync();

    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteOrchestratorServiceInstanceAsync(
        Resource resource,
        ResourceOrchestratorService service,
        ResourceOrchestratorServiceInstance instance,
        ResourceAction action,
        ResourceOrchestratorReplicaGroup? replicaGroup,
        CancellationToken cancellationToken = default) =>
        TryGetTarget(resource, out var target)
            ? target.ExecuteOrchestratorServiceInstanceAsync(resource, service, instance, action, replicaGroup, cancellationToken)
            : EmptyAsync();

    private bool TryGetTarget(
        Resource resource,
        out IContainerApplicationRuntimeTarget target)
    {
        target = targets.FirstOrDefault(candidate => candidate.CanHandle(resource))!;
        return target is not null;
    }

    private static ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> EmptyAsync() =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
}
