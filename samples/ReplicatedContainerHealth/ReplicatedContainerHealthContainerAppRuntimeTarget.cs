using CloudShell.ResourceModel;
using CloudShell.ControlPlane.Providers;
using CloudShell.Abstractions.ResourceManager;
using GraphResource = CloudShell.ResourceModel.Resource;

internal sealed class ReplicatedContainerHealthContainerAppRuntimeTarget(
    IReplicatedContainerHealthContainerAppRuntimeBridge bridge) :
    IContainerApplicationRuntimeTarget
{
    private const string ApiResourceId = "application.container-app:api";

    public bool CanHandle(GraphResource resource) =>
        string.Equals(resource.EffectiveResourceId, ApiResourceId, StringComparison.OrdinalIgnoreCase);

    public ContainerApplicationRuntimeStatus GetStatus(GraphResource resource) =>
        bridge.GetStatus(resource);

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
        GraphResource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default) =>
        await bridge.ExecuteLifecycleAsync(resource, operationId, cancellationToken);

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyImageAsync(
        GraphResource resource,
        CancellationToken cancellationToken = default) =>
        await bridge.ApplyImageAsync(resource, cancellationToken);

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyReplicasAsync(
        GraphResource resource,
        CancellationToken cancellationToken = default) =>
        await bridge.ApplyReplicasAsync(resource, cancellationToken);

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> PrepareOrchestratorServiceAsync(
        GraphResource resource,
        ResourceOrchestratorService service,
        ResourceOrchestratorReplicaGroup? replicaGroup,
        IReadOnlyList<ResourceOrchestratorServiceRoutingBindingDefinition> routingBindings,
        CancellationToken cancellationToken = default) =>
        await bridge.PrepareOrchestratorServiceAsync(
            resource,
            service,
            replicaGroup,
            routingBindings,
            cancellationToken);

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileOrchestratorServiceRoutingAsync(
        GraphResource resource,
        ResourceOrchestratorService service,
        ResourceOrchestratorReplicaGroup? replicaGroup,
        IReadOnlyList<ResourceOrchestratorServiceRoutingBindingDefinition> routingBindings,
        CancellationToken cancellationToken = default) =>
        await bridge.ReconcileOrchestratorServiceRoutingAsync(
            resource,
            service,
            replicaGroup,
            routingBindings,
            cancellationToken);

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> TearDownOrchestratorServiceRoutingAsync(
        GraphResource resource,
        ResourceOrchestratorService service,
        ResourceOrchestratorReplicaGroup? replicaGroup,
        IReadOnlyList<ResourceOrchestratorServiceRoutingBindingDefinition> routingBindings,
        CancellationToken cancellationToken = default) =>
        await bridge.TearDownOrchestratorServiceRoutingAsync(
            resource,
            service,
            replicaGroup,
            routingBindings,
            cancellationToken);

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteOrchestratorServiceInstanceAsync(
        GraphResource resource,
        ResourceOrchestratorService service,
        ResourceOrchestratorServiceInstance instance,
        ResourceAction action,
        ResourceOrchestratorReplicaGroup? replicaGroup,
        CancellationToken cancellationToken = default) =>
        await bridge.ExecuteOrchestratorServiceInstanceAsync(
            resource,
            service,
            instance,
            action,
            replicaGroup,
            cancellationToken);
}
