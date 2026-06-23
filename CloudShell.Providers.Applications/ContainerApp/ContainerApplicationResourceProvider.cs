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
    IContainerApplicationResourceProviderOperations containerApplications)
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
        containerApplications.CanUpdateImage(resource);

    public Task<ResourceProcedureResult> UpdateImageAsync(
        ResourceProcedureContext context,
        string image,
        bool restartIfRunning,
        string? triggeredBy = null,
        CancellationToken cancellationToken = default,
        int? requestedReplicas = null) =>
        containerApplications.UpdateImageAsync(
            context,
            image,
            restartIfRunning,
            triggeredBy,
            cancellationToken,
            requestedReplicas);

    public bool CanUpdateReplicas(Resource resource) =>
        containerApplications.CanUpdateReplicas(resource);

    public Task<ResourceProcedureResult> UpdateReplicasAsync(
        ResourceProcedureContext context,
        int replicas,
        bool restartIfRunning,
        string? triggeredBy = null,
        CancellationToken cancellationToken = default) =>
        containerApplications.UpdateReplicasAsync(context, replicas, restartIfRunning, triggeredBy, cancellationToken);

    public bool CanExecuteOrchestratorService(
        Resource resource,
        ResourceAction action) =>
        containerApplications.CanExecuteOrchestratorService(resource, action);

    public Task<ResourceOrchestratorService> CreateOrchestratorServiceAsync(
        ResourceProcedureContext context,
        CancellationToken cancellationToken = default) =>
        containerApplications.CreateOrchestratorServiceAsync(context, cancellationToken);

    public bool CanDescribeDeployment(Resource resource) =>
        containerApplications.CanDescribeDeployment(resource);

    public Task<ResourceOrchestratorDeployment?> DescribeDeploymentAsync(
        ResourceProcedureContext context,
        CancellationToken cancellationToken = default) =>
        containerApplications.DescribeDeploymentAsync(context, cancellationToken);

    public Task PrepareOrchestratorServiceAsync(
        ResourceOrchestratorServiceProcedureContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default) =>
        containerApplications.PrepareOrchestratorServiceAsync(context, action, cancellationToken);

    public Task ExecuteOrchestratorServiceInstanceAsync(
        ResourceOrchestratorServiceInstanceContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default) =>
        containerApplications.ExecuteOrchestratorServiceInstanceAsync(context, action, cancellationToken);

    public bool CanDescribeDeploymentTearDown(Resource resource) =>
        containerApplications.CanDescribeDeployment(resource);

    public Task<IReadOnlyList<ResourceOrchestratorReplicaGroupTearDownRequest>> DescribeDeploymentTearDownAsync(
        ResourceProcedureContext context,
        ResourceOrchestratorDeploymentApplyResult applyResult,
        CancellationToken cancellationToken = default) =>
        containerApplications.DescribeDeploymentTearDownAsync(context, applyResult, cancellationToken);
}
