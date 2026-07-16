namespace CloudShell.ControlPlane.Providers;

public sealed class ServiceReconcileOperationProvider :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly IProviderExecutionDispatcher _dispatcher;
    private readonly IServiceReconciler? _reconciler;

    public ServiceReconcileOperationProvider(
        IServiceReconciler? reconciler = null,
        IProviderExecutionDispatcher? dispatcher = null)
    {
        _reconciler = reconciler;
        _dispatcher = dispatcher ?? new InProcessProviderExecutionDispatcher(
            [
                new ServiceReconcileExecutionHandler(reconciler)
            ]);
    }

    public ResourceOperationId OperationId =>
        ServiceResourceTypeProvider.Operations.Reconcile;

    public ResourceDefinitionValueSource ResolutionLevel =>
        ResourceDefinitionValueSource.TypeDefinition;

    public bool CanHandle(
        Resource resource,
        ResourceOperationResolution operation) =>
        resource.Type.TypeId == ServiceResourceTypeProvider.ResourceTypeId &&
        operation.IsAvailable;

    public ValueTask<ResourceDefinitionValidationResult> ValidateAsync(
        Resource resource,
        ResourceOperationResolution operation,
        ResourceProviderContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(ResourceDefinitionValidationResult.Success);

    public bool CanProject(
        Resource resource,
        ResourceOperationResolution operation) =>
        CanHandle(resource, operation);

    public ValueTask<IResourceOperationProjection> ProjectAsync(
        Resource resource,
        ResourceOperationResolution operation,
        ResourceOperationProjectionContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IResourceOperationProjection>(
            new ServiceReconcileOperation(
                context.ExecutionContext ?? new ResourceProjectionExecutionContext(resource),
                operation,
                _dispatcher,
                GetUnavailableReason(resource)));

    private string? GetUnavailableReason(Resource resource) =>
        ServiceReconcilerReadiness.IsMissing(_reconciler)
            ? ServiceReconcilerReadiness.CreateMissingReason(resource)
            : null;
}

public sealed class ServiceReconcileOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    IProviderExecutionDispatcher dispatcher,
    string? unavailableReason = null) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly IProviderExecutionDispatcher _dispatcher = dispatcher;

    public Resource Resource => Context.Resource;

    public ResourceOperationResolution Definition { get; } = operation;

    public ResourceOperationId OperationId => ServiceResourceTypeProvider.Operations.Reconcile;

    public bool IsAvailable =>
        Definition.IsAvailable &&
        string.IsNullOrWhiteSpace(UnavailableReason);

    public string? UnavailableReason { get; } =
        string.IsNullOrWhiteSpace(unavailableReason)
            ? operation.UnavailableReason
            : unavailableReason;

    public ValueTask<bool> CanExecuteAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(IsAvailable);

    public ServiceReconcilePlan PlanReconcile() =>
        new(
            Resource,
            Resource.Attributes.GetString(ServiceResourceTypeProvider.Attributes.RoutingMode),
            Resource.State.StartupDependencies);

    public async ValueTask<ResourceOperationExecutionResult> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        if (!await CanExecuteAsync(cancellationToken))
        {
            return new ResourceOperationExecutionResult(
                Resource,
                OperationId,
                [
                    ResourceDefinitionDiagnostic.Error(
                        "service.reconcileUnavailable",
                        UnavailableReason ?? "The service reconcile operation is not available.",
                        OperationId)
                ]);
        }

        var result = await _dispatcher.ExecuteAsync(
            ProviderExecutionRequests.CreateForResource(
                Resource,
                OperationId.Value,
                ProviderExecutionInstructionTypes.ServiceReconcile,
                [ProviderExecutionCapabilities.ServiceReconciliation],
                Context.Resources),
            cancellationToken);

        return result.ToResourceOperationExecutionResult(Resource, OperationId);
    }
}

public sealed record ServiceReconcilePlan(
    Resource Resource,
    string? RoutingMode,
    IReadOnlyList<ResourceReference> References);
