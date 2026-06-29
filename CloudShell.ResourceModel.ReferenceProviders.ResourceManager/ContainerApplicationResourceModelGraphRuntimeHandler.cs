using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ResourceModel.ReferenceProviders.ResourceManager;

public interface IContainerApplicationOrchestratorRuntimeHandler
{
    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> PrepareOrchestratorServiceAsync(
        Resource resource,
        ResourceOrchestratorService service,
        ResourceOrchestratorReplicaGroup? replicaGroup,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileOrchestratorServiceRoutingAsync(
        Resource resource,
        ResourceOrchestratorService service,
        ResourceOrchestratorReplicaGroup? replicaGroup,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> TearDownOrchestratorServiceRoutingAsync(
        Resource resource,
        ResourceOrchestratorService service,
        ResourceOrchestratorReplicaGroup? replicaGroup,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteOrchestratorServiceInstanceAsync(
        Resource resource,
        ResourceOrchestratorService service,
        ResourceOrchestratorServiceInstance instance,
        ResourceAction action,
        ResourceOrchestratorReplicaGroup? replicaGroup,
        CancellationToken cancellationToken = default);
}
