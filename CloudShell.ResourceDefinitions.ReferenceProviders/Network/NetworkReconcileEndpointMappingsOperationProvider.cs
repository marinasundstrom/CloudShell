namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public interface INetworkEndpointMappingReconciler
{
    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileEndpointMappingsAsync(
        Resource resource,
        CancellationToken cancellationToken = default);
}

public sealed class NoopNetworkEndpointMappingReconciler :
    INetworkEndpointMappingReconciler
{
    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileEndpointMappingsAsync(
        Resource resource,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
}

public sealed class NetworkReconcileEndpointMappingsOperationProvider(
    INetworkEndpointMappingReconciler? reconciler = null) :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly INetworkEndpointMappingReconciler _reconciler =
        reconciler ?? new NoopNetworkEndpointMappingReconciler();

    public ResourceOperationId OperationId =>
        NetworkResourceTypeProvider.Operations.ReconcileEndpointMappings;

    public ResourceDefinitionValueSource ResolutionLevel =>
        ResourceDefinitionValueSource.TypeDefinition;

    public bool CanHandle(
        Resource resource,
        ResourceOperationResolution operation) =>
        resource.Type.TypeId == NetworkResourceTypeProvider.ResourceTypeId &&
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
            new NetworkReconcileEndpointMappingsOperation(
                context.ExecutionContext ?? new ResourceProjectionExecutionContext(resource),
                operation,
                _reconciler));
}

public sealed class NetworkReconcileEndpointMappingsOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    INetworkEndpointMappingReconciler reconciler) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly INetworkEndpointMappingReconciler _reconciler =
        reconciler;

    public Resource Resource => Context.Resource;

    public ResourceOperationResolution Definition { get; } = operation;

    public ResourceOperationId OperationId => NetworkResourceTypeProvider.Operations.ReconcileEndpointMappings;

    public bool IsAvailable => Definition.IsAvailable;

    public string? UnavailableReason => Definition.UnavailableReason;

    public ValueTask<bool> CanExecuteAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(IsAvailable);

    public NetworkReconcileEndpointMappingsPlan PlanReconcile() =>
        new(
            Resource,
            Resource.Attributes.GetString(NetworkResourceTypeProvider.Attributes.MappingProviders));

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
                        "network.reconcileEndpointMappingsUnavailable",
                        UnavailableReason ?? "The network endpoint-mapping reconcile operation is not available.",
                        OperationId)
                ]);
        }

        var diagnostics = await _reconciler.ReconcileEndpointMappingsAsync(
            Resource,
            cancellationToken);

        return new ResourceOperationExecutionResult(
            Resource,
            OperationId,
            diagnostics);
    }
}

public sealed record NetworkReconcileEndpointMappingsPlan(
    Resource Resource,
    string? MappingProviders);
