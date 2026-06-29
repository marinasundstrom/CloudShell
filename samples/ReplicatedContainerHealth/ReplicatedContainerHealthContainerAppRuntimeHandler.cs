using CloudShell.ResourceModel;
using CloudShell.ControlPlane.Providers;
using CloudShell.Abstractions.ResourceManager;
using GraphResource = CloudShell.ResourceModel.Resource;

internal sealed class ReplicatedContainerHealthContainerAppRuntimeHandler(
    IReplicatedContainerHealthContainerAppRuntimeBridge bridge) :
    IContainerApplicationRuntimeHandler,
    IContainerApplicationOrchestratorRuntimeHandler
{
    private const string ApiResourceId = "application.container-app:api";

    public ContainerApplicationRuntimeStatus GetStatus(GraphResource resource) =>
        IsApiResource(resource)
            ? bridge.GetStatus(resource)
            : ContainerApplicationRuntimeStatus.Unknown;

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
        GraphResource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default)
    {
        if (!IsApiResource(resource))
        {
            return [];
        }

        return await bridge.ExecuteLifecycleAsync(resource, operationId, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyImageAsync(
        GraphResource resource,
        CancellationToken cancellationToken = default)
    {
        if (!IsApiResource(resource))
        {
            return [];
        }

        return await bridge.ApplyImageAsync(resource, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyReplicasAsync(
        GraphResource resource,
        CancellationToken cancellationToken = default)
    {
        if (!IsApiResource(resource))
        {
            return [];
        }

        return await bridge.ApplyReplicasAsync(resource, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> PrepareOrchestratorServiceAsync(
        GraphResource resource,
        ResourceOrchestratorService service,
        ResourceOrchestratorReplicaGroup? replicaGroup,
        CancellationToken cancellationToken = default)
    {
        if (!IsApiResource(resource))
        {
            return [];
        }

        return await bridge.PrepareOrchestratorServiceAsync(
            resource,
            service,
            replicaGroup,
            cancellationToken);
    }

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileOrchestratorServiceRoutingAsync(
        GraphResource resource,
        ResourceOrchestratorService service,
        ResourceOrchestratorReplicaGroup? replicaGroup,
        CancellationToken cancellationToken = default)
    {
        if (!IsApiResource(resource))
        {
            return [];
        }

        return await bridge.ReconcileOrchestratorServiceRoutingAsync(
            resource,
            service,
            replicaGroup,
            cancellationToken);
    }

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> TearDownOrchestratorServiceRoutingAsync(
        GraphResource resource,
        ResourceOrchestratorService service,
        ResourceOrchestratorReplicaGroup? replicaGroup,
        CancellationToken cancellationToken = default)
    {
        if (!IsApiResource(resource))
        {
            return [];
        }

        return await bridge.TearDownOrchestratorServiceRoutingAsync(
            resource,
            service,
            replicaGroup,
            cancellationToken);
    }

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteOrchestratorServiceInstanceAsync(
        GraphResource resource,
        ResourceOrchestratorService service,
        ResourceOrchestratorServiceInstance instance,
        ResourceAction action,
        ResourceOrchestratorReplicaGroup? replicaGroup,
        CancellationToken cancellationToken = default)
    {
        if (!IsApiResource(resource))
        {
            return [];
        }

        return await bridge.ExecuteOrchestratorServiceInstanceAsync(
            resource,
            service,
            instance,
            action,
            replicaGroup,
            cancellationToken);
    }

    private static bool IsApiResource(GraphResource resource) =>
        string.Equals(resource.EffectiveResourceId, ApiResourceId, StringComparison.OrdinalIgnoreCase);
}
