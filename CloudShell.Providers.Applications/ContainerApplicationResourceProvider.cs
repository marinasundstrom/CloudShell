using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

internal sealed class ContainerApplicationResourceProvider(
    IApplicationResourceProjectionSource projections,
    ApplicationResourceService applications)
    : ApplicationResourceTypeProvider(projections, applications),
    IResourceImageUpdateProvider,
    IResourceReplicaUpdateProvider,
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
        Applications.CanUpdateImage(resource);

    public Task<ResourceProcedureResult> UpdateImageAsync(
        ResourceProcedureContext context,
        string image,
        bool restartIfRunning,
        string? triggeredBy = null,
        CancellationToken cancellationToken = default) =>
        Applications.UpdateImageAsync(context, image, restartIfRunning, triggeredBy, cancellationToken);

    public bool CanUpdateReplicas(Resource resource) =>
        Applications.CanUpdateReplicas(resource);

    public Task<ResourceProcedureResult> UpdateReplicasAsync(
        ResourceProcedureContext context,
        int replicas,
        bool restartIfRunning,
        string? triggeredBy = null,
        CancellationToken cancellationToken = default) =>
        Applications.UpdateReplicasAsync(context, replicas, restartIfRunning, triggeredBy, cancellationToken);

    public bool CanExecuteOrchestratorService(
        Resource resource,
        ResourceAction action) =>
        Applications.CanExecuteOrchestratorService(resource, action);

    public Task<ResourceOrchestratorService> CreateOrchestratorServiceAsync(
        ResourceProcedureContext context,
        CancellationToken cancellationToken = default) =>
        Applications.CreateOrchestratorServiceAsync(context, cancellationToken);

    public Task PrepareOrchestratorServiceAsync(
        ResourceOrchestratorServiceProcedureContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default) =>
        Applications.PrepareOrchestratorServiceAsync(context, action, cancellationToken);

    public Task ExecuteOrchestratorServiceInstanceAsync(
        ResourceOrchestratorServiceInstanceContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default) =>
        Applications.ExecuteOrchestratorServiceInstanceAsync(context, action, cancellationToken);
}
