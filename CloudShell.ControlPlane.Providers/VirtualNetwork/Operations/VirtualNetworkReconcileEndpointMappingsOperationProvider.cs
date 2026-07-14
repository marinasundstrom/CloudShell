namespace CloudShell.ControlPlane.Providers;

public sealed class VirtualNetworkReconcileEndpointMappingsOperationProvider :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly IProviderExecutionDispatcher _dispatcher;

    public VirtualNetworkReconcileEndpointMappingsOperationProvider(
        IProviderExecutionDispatcher? dispatcher = null,
        IVirtualNetworkEndpointMappingReconciler? reconciler = null)
    {
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
                _dispatcher));
}

public sealed class VirtualNetworkReconcileEndpointMappingsOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    IProviderExecutionDispatcher dispatcher) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly IProviderExecutionDispatcher _dispatcher = dispatcher;

    public Resource Resource => Context.Resource;

    public ResourceOperationResolution Definition { get; } = operation;

    public ResourceOperationId OperationId => VirtualNetworkResourceTypeProvider.Operations.ReconcileEndpointMappings;

    public bool IsAvailable => Definition.IsAvailable;

    public string? UnavailableReason => Definition.UnavailableReason;

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
            new ProviderExecutionRequest
            {
                AssignmentId = $"{Resource.EffectiveResourceId}:{OperationId}",
                InstructionType = ProviderExecutionInstructionTypes.VirtualNetworkEndpointReconcile,
                TargetResourceId = Resource.EffectiveResourceId,
                DesiredGeneration = Resource.Revision.Value,
                IdempotencyKey = $"{Resource.EffectiveResourceId}:{OperationId}:{Resource.Revision.Value}",
                RequiredCapabilities = [ProviderExecutionCapabilities.VirtualNetworking],
                TargetResourceSnapshot = Resource,
                ResourceSnapshot = Context.Resources,
                RequestedAt = DateTimeOffset.UtcNow
            },
            cancellationToken);

        return new ResourceOperationExecutionResult(
            Resource,
            OperationId,
            result.Diagnostics);
    }
}

public sealed record VirtualNetworkReconcileEndpointMappingsPlan(
    Resource Resource,
    string? MappingProviders,
    string? HostReadiness);
