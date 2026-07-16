namespace CloudShell.ControlPlane.Providers;

public sealed class ContainerHostInspectOperationProvider :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly IProviderExecutionDispatcher _dispatcher;
    private readonly IContainerHostInspector? _inspector;

    public ContainerHostInspectOperationProvider(
        IContainerHostInspector? inspector = null,
        IProviderExecutionDispatcher? dispatcher = null)
    {
        _inspector = inspector;
        _dispatcher = dispatcher ?? new InProcessProviderExecutionDispatcher(
            [
                new ContainerHostInspectExecutionHandler(inspector)
            ]);
    }

    public ResourceOperationId OperationId =>
        ContainerHostResourceTypeProvider.Operations.Inspect;

    public ResourceDefinitionValueSource ResolutionLevel =>
        ResourceDefinitionValueSource.TypeDefinition;

    public bool CanHandle(
        Resource resource,
        ResourceOperationResolution operation) =>
        resource.Type.TypeId == ContainerHostResourceTypeProvider.ResourceTypeId &&
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
            new ContainerHostInspectOperation(
                context.ExecutionContext ?? new ResourceProjectionExecutionContext(resource),
                operation,
                _dispatcher,
                GetUnavailableReason(resource)));

    private string? GetUnavailableReason(Resource resource) =>
        ContainerHostInspectorReadiness.IsMissing(_inspector)
            ? ContainerHostInspectorReadiness.CreateMissingReason(resource)
            : null;
}

public sealed class ContainerHostInspectOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    IProviderExecutionDispatcher dispatcher,
    string? unavailableReason = null) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly IProviderExecutionDispatcher _dispatcher = dispatcher;

    public Resource Resource => Context.Resource;

    public ResourceOperationResolution Definition { get; } = operation;

    public ResourceOperationId OperationId => ContainerHostResourceTypeProvider.Operations.Inspect;

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

    public ContainerHostInspectionPlan PlanInspection() =>
        new(
            Resource,
            Resource.Attributes.GetString(ContainerHostResourceTypeProvider.Attributes.HostKind),
            Resource.Attributes.GetString(ContainerHostResourceTypeProvider.Attributes.Endpoint));

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
                        "application.containerHost.inspectUnavailable",
                        UnavailableReason ?? "The container host inspect operation is not available.",
                        OperationId)
                ]);
        }

        var result = await _dispatcher.ExecuteAsync(
            ProviderExecutionRequests.CreateForResource(
                Resource,
                OperationId.Value,
                ProviderExecutionInstructionTypes.ContainerHostInspect,
                [ProviderExecutionCapabilities.RuntimeObservation],
                Context.Resources),
            cancellationToken);

        return result.ToResourceOperationExecutionResult(Resource, OperationId);
    }
}

public sealed record ContainerHostInspectionPlan(
    Resource Resource,
    string? HostKind,
    string? Endpoint);
