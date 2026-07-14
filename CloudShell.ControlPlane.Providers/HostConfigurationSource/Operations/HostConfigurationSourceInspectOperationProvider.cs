namespace CloudShell.ControlPlane.Providers;

public sealed class HostConfigurationSourceInspectOperationProvider :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly IProviderExecutionDispatcher _dispatcher;

    public HostConfigurationSourceInspectOperationProvider(
        IHostConfigurationSourceInspector? inspector = null,
        IProviderExecutionDispatcher? dispatcher = null)
    {
        _dispatcher = dispatcher ?? new InProcessProviderExecutionDispatcher(
            [
                new HostConfigurationSourceInspectExecutionHandler(inspector)
            ]);
    }

    public ResourceOperationId OperationId =>
        HostConfigurationSourceResourceTypeProvider.Operations.Inspect;

    public ResourceDefinitionValueSource ResolutionLevel =>
        ResourceDefinitionValueSource.TypeDefinition;

    public bool CanHandle(
        Resource resource,
        ResourceOperationResolution operation) =>
        resource.Type.TypeId == HostConfigurationSourceResourceTypeProvider.ResourceTypeId &&
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
            new HostConfigurationSourceInspectOperation(
                context.ExecutionContext ?? new ResourceProjectionExecutionContext(resource),
                operation,
                _dispatcher));
}

public sealed class HostConfigurationSourceInspectOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    IProviderExecutionDispatcher dispatcher) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly IProviderExecutionDispatcher _dispatcher = dispatcher;

    public Resource Resource => Context.Resource;

    public ResourceOperationResolution Definition { get; } = operation;

    public ResourceOperationId OperationId => HostConfigurationSourceResourceTypeProvider.Operations.Inspect;

    public bool IsAvailable => Definition.IsAvailable;

    public string? UnavailableReason => Definition.UnavailableReason;

    public ValueTask<bool> CanExecuteAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(IsAvailable);

    public HostConfigurationSourceInspectionPlan PlanInspection() =>
        new(
            Resource,
            Resource.Attributes.GetString(HostConfigurationSourceResourceTypeProvider.Attributes.Source),
            GetEntryCount(Resource));

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
                        "configuration.host.inspectUnavailable",
                        UnavailableReason ?? "The host configuration source inspect operation is not available.",
                        OperationId)
                ]);
        }

        var result = await _dispatcher.ExecuteAsync(
            ProviderExecutionRequests.CreateForResource(
                Resource,
                OperationId.Value,
                ProviderExecutionInstructionTypes.HostConfigurationSourceInspect,
                [ProviderExecutionCapabilities.RuntimeObservation],
                Context.Resources),
            cancellationToken);

        return result.ToResourceOperationExecutionResult(Resource, OperationId);
    }

    private static int GetEntryCount(Resource resource) =>
        int.TryParse(
            resource.Attributes.GetString(HostConfigurationSourceResourceTypeProvider.Attributes.EntryCount),
            out var entryCount)
                ? entryCount
                : 0;
}

public sealed record HostConfigurationSourceInspectionPlan(
    Resource Resource,
    string? Source,
    int EntryCount);
