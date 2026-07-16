namespace CloudShell.ControlPlane.Providers;

public sealed class VirtualNetworkReconcileEndpointMappingsOperationProvider :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly IProviderExecutionDispatcher _dispatcher;
    private readonly IVirtualNetworkEndpointMappingReconciler? _reconciler;

    public VirtualNetworkReconcileEndpointMappingsOperationProvider(
        IProviderExecutionDispatcher? dispatcher = null,
        IVirtualNetworkEndpointMappingReconciler? reconciler = null)
    {
        _reconciler = reconciler;
        _dispatcher = dispatcher ??
            new InProcessProviderExecutionDispatcher(
                [new VirtualNetworkEndpointMappingExecutionHandler(reconciler)]);
    }

    public ResourceOperationId OperationId =>
        VirtualNetworkResourceTypeProvider.Operations.ReconcileEndpointMappings;

    public ResourceDefinitionValueSource ResolutionLevel =>
        ResourceDefinitionValueSource.TypeDefinition;

    public bool CanHandle(
        Resource resource,
        ResourceOperationResolution operation) =>
        resource.Type.TypeId == VirtualNetworkResourceTypeProvider.ResourceTypeId &&
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
            new VirtualNetworkReconcileEndpointMappingsOperation(
                context.ExecutionContext ?? new ResourceProjectionExecutionContext(resource),
                operation,
                _dispatcher,
                GetUnavailableReason(resource)));

    private string? GetUnavailableReason(Resource resource) =>
        VirtualNetworkEndpointMappingReconcilerReadiness.IsMissing(_reconciler)
            ? VirtualNetworkEndpointMappingReconcilerReadiness.CreateMissingReason(resource)
            : null;
}

public sealed class VirtualNetworkReconcileEndpointMappingsOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    IProviderExecutionDispatcher dispatcher,
    string? unavailableReason = null) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly IProviderExecutionDispatcher _dispatcher = dispatcher;

    public Resource Resource => Context.Resource;

    public ResourceOperationResolution Definition { get; } = operation;

    public ResourceOperationId OperationId => VirtualNetworkResourceTypeProvider.Operations.ReconcileEndpointMappings;

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

    public VirtualNetworkReconcileEndpointMappingsPlan PlanReconcile() =>
        new(
            Resource,
            Resource.Attributes.GetString(VirtualNetworkResourceTypeProvider.Attributes.MappingProviders),
            Resource.Attributes.GetString(VirtualNetworkResourceTypeProvider.Attributes.HostReadiness));

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
                        "network.virtual.reconcileEndpointMappingsUnavailable",
                        UnavailableReason ?? "The virtual network endpoint-mapping reconcile operation is not available.",
                        OperationId)
                ]);
        }

        var result = await _dispatcher.ExecuteAsync(
            ProviderExecutionRequests.CreateForResource(
                Resource,
                OperationId.Value,
                ProviderExecutionInstructionTypes.VirtualNetworkEndpointReconcile,
                [ProviderExecutionCapabilities.VirtualNetworking],
                Context.Resources),
            cancellationToken);

        return result.ToResourceOperationExecutionResult(Resource, OperationId);
    }
}

public sealed record VirtualNetworkReconcileEndpointMappingsPlan(
    Resource Resource,
    string? MappingProviders,
    string? HostReadiness);
