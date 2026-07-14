namespace CloudShell.ControlPlane.Providers;

public sealed class DnsZoneReconcileNameMappingsOperationProvider :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly IProviderExecutionDispatcher _dispatcher;

    public DnsZoneReconcileNameMappingsOperationProvider(
        IProviderExecutionDispatcher? dispatcher = null,
        IDnsZoneNameMappingReconciler? nameMappingReconciler = null)
    {
        _dispatcher = dispatcher ??
            new InProcessProviderExecutionDispatcher(
                [new DnsZoneNameMappingExecutionHandler(nameMappingReconciler)]);
    }

    public ResourceOperationId OperationId =>
        DnsZoneResourceTypeProvider.Operations.ReconcileNameMappings;

    public ResourceDefinitionValueSource ResolutionLevel =>
        ResourceDefinitionValueSource.TypeDefinition;

    public bool CanHandle(
        Resource resource,
        ResourceOperationResolution operation) =>
        resource.Type.TypeId == DnsZoneResourceTypeProvider.ResourceTypeId &&
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
            new DnsZoneReconcileNameMappingsOperation(
                context.ExecutionContext ?? new ResourceProjectionExecutionContext(resource),
                operation,
                _dispatcher));
}

public sealed class DnsZoneReconcileNameMappingsOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    IProviderExecutionDispatcher dispatcher) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly IProviderExecutionDispatcher _dispatcher = dispatcher;

    public Resource Resource => Context.Resource;

    public ResourceOperationResolution Definition { get; } = operation;

    public ResourceOperationId OperationId => DnsZoneResourceTypeProvider.Operations.ReconcileNameMappings;

    public bool IsAvailable => Definition.IsAvailable;

    public string? UnavailableReason => Definition.UnavailableReason;

    public ValueTask<bool> CanExecuteAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(IsAvailable);

    public DnsZoneReconcileNameMappingsPlan PlanReconcile() =>
        new(
            Resource,
            Resource.Attributes.GetString(DnsZoneResourceTypeProvider.Attributes.ZoneName),
            Resource.Attributes.GetString(DnsZoneResourceTypeProvider.Attributes.Provider));

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
                        "dns.zone.reconcileNameMappingsUnavailable",
                        UnavailableReason ?? "The DNS zone name-mapping reconcile operation is not available.",
                        OperationId)
                ]);
        }

        var result = await _dispatcher.ExecuteAsync(
            ProviderExecutionRequests.CreateForResource(
                Resource,
                OperationId.Value,
                ProviderExecutionInstructionTypes.DnsNameMappingReconcile,
                [ProviderExecutionCapabilities.DnsNameMappings],
                Context.Resources),
            cancellationToken);

        return new ResourceOperationExecutionResult(
            Resource,
            OperationId,
            result.Diagnostics);
    }
}

public sealed record DnsZoneReconcileNameMappingsPlan(
    Resource Resource,
    string? ZoneName,
    string? Provider);
