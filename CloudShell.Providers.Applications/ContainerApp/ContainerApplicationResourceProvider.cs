using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

internal sealed class ContainerApplicationResourceProvider(
    IApplicationResourceProjectionSource projections,
    IApplicationResourceDefinitionSource definitions,
    IApplicationResourceProcedureOperations procedures,
    IApplicationResourceTemplateOperations templates,
    IApplicationResourceDeclarationOperations declarations,
    IApplicationResourceDescriptorOperations descriptors,
    IApplicationResourceActionAvailabilityOperations actions,
    IContainerApplicationUpdateOperations containerApplicationUpdates,
    IContainerApplicationOrchestratorServiceDescriptionOperations containerApplicationServiceDescriptions,
    IContainerApplicationOrchestrationOperations containerApplicationOrchestration,
    IContainerApplicationDeploymentDescriptionOperations containerApplicationDeploymentDescriptions,
    IContainerApplicationDeploymentOutcomeOperations containerApplicationDeploymentOutcomes)
    : ApplicationResourceTypeProvider(
        projections,
        definitions,
        procedures,
        templates,
        declarations,
        descriptors,
        actions),
    IResourceImageUpdateProvider,
    IResourceReplicaUpdateProvider,
    IResourceOrchestratorDeploymentProvider,
    IResourceOrchestratorDeploymentTearDownProvider,
    IResourceOrchestratorDeploymentAppliedProvider,
    IResourceOrchestratorDeploymentFailureProvider,
    IResourceOrchestratorServiceProcedureProvider
{
    public const string ProviderId = ApplicationResourceProviderIds.ContainerApplication;

    public override string Id => ProviderId;

    protected override ApplicationResourceProjection Projection { get; } = new(
        application => ApplicationResourceTypes.IsContainerApp(application.ResourceType),
        _ => "Container app",
        ApplicationResourceProjectionSupport.GetContainerVersion,
        ApplicationResourceProjectionSupport.GetContainerWorkloadKind,
        _ => ResourceClass.Container);

    public bool CanUpdateImage(Resource resource) =>
        containerApplicationUpdates.CanUpdateImage(resource);

    public Task<ResourceProcedureResult> UpdateImageAsync(
        ResourceProcedureContext context,
        string image,
        bool restartIfRunning,
        string? triggeredBy = null,
        CancellationToken cancellationToken = default,
        int? requestedReplicas = null) =>
        containerApplicationUpdates.UpdateImageAsync(
            context,
            image,
            restartIfRunning,
            triggeredBy,
            cancellationToken,
            requestedReplicas);

    public bool CanUpdateReplicas(Resource resource) =>
        containerApplicationUpdates.CanUpdateReplicas(resource);

    public Task<ResourceProcedureResult> UpdateReplicasAsync(
        ResourceProcedureContext context,
        int replicas,
        bool restartIfRunning,
        string? triggeredBy = null,
        CancellationToken cancellationToken = default) =>
        containerApplicationUpdates.UpdateReplicasAsync(context, replicas, restartIfRunning, triggeredBy, cancellationToken);

    public bool CanExecuteOrchestratorService(
        Resource resource,
        ResourceAction action) =>
        containerApplicationServiceDescriptions.CanExecuteOrchestratorService(resource, action);

    public Task<ResourceOrchestratorService> CreateOrchestratorServiceAsync(
        ResourceProcedureContext context,
        CancellationToken cancellationToken = default) =>
        containerApplicationServiceDescriptions.CreateOrchestratorServiceAsync(context, cancellationToken);

    public bool CanDescribeDeployment(Resource resource) =>
        containerApplicationDeploymentDescriptions.CanDescribeDeployment(resource);

    public Task<ResourceOrchestratorDeployment?> DescribeDeploymentAsync(
        ResourceProcedureContext context,
        CancellationToken cancellationToken = default) =>
        containerApplicationDeploymentDescriptions.DescribeDeploymentAsync(context, cancellationToken);

    public Task PrepareOrchestratorServiceAsync(
        ResourceOrchestratorServiceProcedureContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default) =>
        containerApplicationOrchestration.PrepareOrchestratorServiceAsync(context, action, cancellationToken);

    public Task ExecuteOrchestratorServiceInstanceAsync(
        ResourceOrchestratorServiceInstanceContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default) =>
        containerApplicationOrchestration.ExecuteOrchestratorServiceInstanceAsync(context, action, cancellationToken);

    public bool CanDescribeDeploymentTearDown(Resource resource) =>
        containerApplicationDeploymentOutcomes.CanDescribeDeploymentTearDown(resource);

    public Task<IReadOnlyList<ResourceOrchestratorReplicaGroupTearDownRequest>> DescribeDeploymentTearDownAsync(
        ResourceProcedureContext context,
        ResourceOrchestratorDeploymentApplyResult applyResult,
        CancellationToken cancellationToken = default) =>
        containerApplicationDeploymentOutcomes.DescribeDeploymentTearDownAsync(context, applyResult, cancellationToken);

    public bool CanHandleDeploymentApplied(Resource resource) =>
        containerApplicationDeploymentOutcomes.CanHandleDeploymentApplied(resource);

    public Task HandleDeploymentAppliedAsync(
        ResourceProcedureContext context,
        ResourceOrchestratorDeploymentApplyResult applyResult,
        CancellationToken cancellationToken = default) =>
        containerApplicationDeploymentOutcomes.HandleDeploymentAppliedAsync(context, applyResult, cancellationToken);

    public bool CanHandleDeploymentApplyFailed(Resource resource) =>
        containerApplicationDeploymentOutcomes.CanHandleDeploymentApplyFailed(resource);

    public Task HandleDeploymentApplyFailedAsync(
        ResourceProcedureContext context,
        ResourceOrchestratorDeployment deployment,
        Exception exception,
        CancellationToken cancellationToken = default) =>
        containerApplicationDeploymentOutcomes.HandleDeploymentApplyFailedAsync(context, deployment, exception, cancellationToken);
}
