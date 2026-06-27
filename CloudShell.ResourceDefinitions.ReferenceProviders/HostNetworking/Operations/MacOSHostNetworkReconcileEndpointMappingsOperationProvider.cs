namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class MacOSHostNetworkReconcileEndpointMappingsOperationProvider(
    IMacOSHostNetworkEndpointMappingReconciler? reconciler = null) :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly IMacOSHostNetworkEndpointMappingReconciler _reconciler =
        reconciler ?? new NoopMacOSHostNetworkEndpointMappingReconciler();

    public ResourceOperationId OperationId =>
        MacOSHostNetworkResourceTypeProvider.Operations.ReconcileEndpointMappings;

    public ResourceDefinitionValueSource ResolutionLevel =>
        ResourceDefinitionValueSource.TypeDefinition;

    public bool CanHandle(
        Resource resource,
        ResourceOperationResolution operation) =>
        resource.Type.TypeId == MacOSHostNetworkResourceTypeProvider.ResourceTypeId &&
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
            new MacOSHostNetworkReconcileEndpointMappingsOperation(
                context.ExecutionContext ?? new ResourceProjectionExecutionContext(resource),
                operation,
                _reconciler));
}

public sealed class MacOSHostNetworkReconcileEndpointMappingsOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    IMacOSHostNetworkEndpointMappingReconciler reconciler) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly IMacOSHostNetworkEndpointMappingReconciler _reconciler =
        reconciler;

    public Resource Resource => Context.Resource;

    public ResourceOperationResolution Definition { get; } = operation;

    public ResourceOperationId OperationId =>
        MacOSHostNetworkResourceTypeProvider.Operations.ReconcileEndpointMappings;

    public bool IsAvailable => Definition.IsAvailable;

    public string? UnavailableReason => Definition.UnavailableReason;

    public ValueTask<bool> CanExecuteAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(IsAvailable);

    public MacOSHostNetworkReconcileEndpointMappingsPlan PlanReconcile() =>
        new(
            Resource,
            Resource.Attributes.GetString(MacOSHostNetworkResourceTypeProvider.Attributes.NetworkingMode),
            Resource.Attributes.GetString(MacOSHostNetworkResourceTypeProvider.Attributes.HostOperatingSystem));

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
                        "hostNetworking.macos.reconcileEndpointMappingsUnavailable",
                        UnavailableReason ?? "The macOS host networking endpoint-mapping reconcile operation is not available.",
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

public sealed record MacOSHostNetworkReconcileEndpointMappingsPlan(
    Resource Resource,
    string? NetworkingMode,
    string? HostOperatingSystem);
