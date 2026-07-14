namespace CloudShell.ControlPlane.Providers;

public sealed class MacOSHostNetworkReconcileEndpointMappingsOperationProvider(
    IProviderExecutionDispatcher? dispatcher = null,
    IMacOSHostNetworkEndpointMappingReconciler? reconciler = null) :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly IProviderExecutionDispatcher _dispatcher =
        dispatcher ?? new InProcessProviderExecutionDispatcher(
            [new MacOSHostNetworkEndpointMappingExecutionHandler(reconciler)]);

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
                _dispatcher));
}

public sealed class MacOSHostNetworkReconcileEndpointMappingsOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    IProviderExecutionDispatcher dispatcher) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly IProviderExecutionDispatcher _dispatcher = dispatcher;

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

        var result = await _dispatcher.ExecuteAsync(
            ProviderExecutionRequests.CreateForResource(
                Resource,
                OperationId.Value,
                ProviderExecutionInstructionTypes.MacOSHostNetworkEndpointReconcile,
                [ProviderExecutionCapabilities.HostNetworking],
                Context.Resources),
            cancellationToken);

        return result.ToResourceOperationExecutionResult(Resource, OperationId);
    }
}

public sealed record MacOSHostNetworkReconcileEndpointMappingsPlan(
    Resource Resource,
    string? NetworkingMode,
    string? HostOperatingSystem);
