namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class ServiceReconcileOperationProvider(
    IServiceReconciler? reconciler = null) :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly IServiceReconciler _reconciler =
        reconciler ?? new NoopServiceReconciler();

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
                _reconciler));
}

public sealed class ServiceReconcileOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    IServiceReconciler reconciler) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly IServiceReconciler _reconciler = reconciler;

    public Resource Resource => Context.Resource;

    public ResourceOperationResolution Definition { get; } = operation;

    public ResourceOperationId OperationId => ServiceResourceTypeProvider.Operations.Reconcile;

    public bool IsAvailable => Definition.IsAvailable;

    public string? UnavailableReason => Definition.UnavailableReason;

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

        var diagnostics = await _reconciler.ReconcileAsync(
            Resource,
            cancellationToken);

        return new ResourceOperationExecutionResult(
            Resource,
            OperationId,
            diagnostics);
    }
}

public sealed record ServiceReconcilePlan(
    Resource Resource,
    string? RoutingMode,
    IReadOnlyList<ResourceReference> References);
