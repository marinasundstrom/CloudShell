using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;

namespace CloudShell.ResourceDefinitions.ResourceManager;

public interface IResourceModelGraphDeploymentDescriptor
{
    bool CanDescribeDeployment(
        ResourceManagerResource resource,
        Resource graphResource);

    ValueTask<CloudShell.Abstractions.ResourceManager.ResourceOrchestratorDeployment?> DescribeDeploymentAsync(
        ResourceModelGraphDeploymentDescriptionContext context,
        CancellationToken cancellationToken = default);
}

public sealed record ResourceModelGraphDeploymentDescriptionContext(
    ResourceManagerResource Resource,
    Resource GraphResource,
    CloudShell.Abstractions.ResourceManager.ResourceProcedureContext ProcedureContext);

public interface IResourceModelGraphOrchestratorServiceExecutor
{
    bool CanExecuteOrchestratorService(
        ResourceManagerResource resource,
        CloudShell.Abstractions.ResourceManager.ResourceAction action);

    ValueTask PrepareOrchestratorServiceAsync(
        ResourceModelGraphOrchestratorServiceProcedureContext context,
        CloudShell.Abstractions.ResourceManager.ResourceAction action,
        CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    ValueTask ReconcileOrchestratorServiceRoutingAsync(
        ResourceModelGraphOrchestratorServiceProcedureContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    ValueTask ExecuteOrchestratorServiceInstanceAsync(
        ResourceModelGraphOrchestratorServiceInstanceContext context,
        CloudShell.Abstractions.ResourceManager.ResourceAction action,
        CancellationToken cancellationToken = default);
}

public sealed record ResourceModelGraphOrchestratorServiceProcedureContext(
    ResourceManagerResource Resource,
    Resource GraphResource,
    CloudShell.Abstractions.ResourceManager.ResourceProcedureContext ProcedureContext,
    CloudShell.Abstractions.ResourceManager.ResourceOrchestratorService Service,
    CloudShell.Abstractions.ResourceManager.ResourceOrchestratorReplicaGroup? ReplicaGroup = null);

public sealed record ResourceModelGraphOrchestratorServiceInstanceContext(
    ResourceManagerResource Resource,
    Resource GraphResource,
    CloudShell.Abstractions.ResourceManager.ResourceProcedureContext ProcedureContext,
    CloudShell.Abstractions.ResourceManager.ResourceOrchestratorService Service,
    CloudShell.Abstractions.ResourceManager.ResourceOrchestratorServiceInstance Instance,
    CloudShell.Abstractions.ResourceManager.ResourceOrchestratorReplicaGroup? ReplicaGroup = null);
