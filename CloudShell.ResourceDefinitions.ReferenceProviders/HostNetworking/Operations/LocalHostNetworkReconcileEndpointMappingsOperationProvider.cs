namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class LocalHostNetworkReconcileEndpointMappingsOperationProvider(
    ILocalHostNetworkEndpointMappingReconciler? reconciler = null) :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly ILocalHostNetworkEndpointMappingReconciler _reconciler =
        reconciler ?? new NoopLocalHostNetworkEndpointMappingReconciler();

    public ResourceOperationId OperationId =>
        LocalHostNetworkResourceTypeProvider.Operations.ReconcileEndpointMappings;

    public ResourceDefinitionValueSource ResolutionLevel =>
        ResourceDefinitionValueSource.TypeDefinition;

    public bool CanHandle(
        Resource resource,
        ResourceOperationResolution operation) =>
        resource.Type.TypeId == LocalHostNetworkResourceTypeProvider.ResourceTypeId &&
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
            new LocalHostNetworkReconcileEndpointMappingsOperation(
                context.ExecutionContext ?? new ResourceProjectionExecutionContext(resource),
                operation,
                _reconciler));
}

public sealed class LocalHostNetworkReconcileEndpointMappingsOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    ILocalHostNetworkEndpointMappingReconciler reconciler) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly ILocalHostNetworkEndpointMappingReconciler _reconciler =
        reconciler;

    public Resource Resource => Context.Resource;

    public ResourceOperationResolution Definition { get; } = operation;

    public ResourceOperationId OperationId =>
        LocalHostNetworkResourceTypeProvider.Operations.ReconcileEndpointMappings;

    public bool IsAvailable => Definition.IsAvailable;

    public string? UnavailableReason => Definition.UnavailableReason;

    public ValueTask<bool> CanExecuteAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(IsAvailable);

    public LocalHostNetworkReconcileEndpointMappingsPlan PlanReconcile() =>
        new(
            Resource,
            Resource.Attributes.GetString(LocalHostNetworkResourceTypeProvider.Attributes.NetworkingMode),
            Resource.Attributes.GetString(LocalHostNetworkResourceTypeProvider.Attributes.HostOperatingSystem));

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
                        "hostNetworking.reconcileEndpointMappingsUnavailable",
                        UnavailableReason ?? "The local host networking endpoint-mapping reconcile operation is not available.",
                        OperationId)
                ]);
        }

        var diagnostics = await _reconciler.ReconcileEndpointMappingsAsync(
            Resource,
            Context,
            cancellationToken);

        return new ResourceOperationExecutionResult(
            Resource,
            OperationId,
            diagnostics);
    }
}

public sealed record LocalHostNetworkReconcileEndpointMappingsPlan(
    Resource Resource,
    string? NetworkingMode,
    string? HostOperatingSystem);
