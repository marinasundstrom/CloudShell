namespace CloudShell.ControlPlane.Providers;

public sealed class ContainerHostInspectOperationProvider(
    IContainerHostInspector? inspector = null) :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly IContainerHostInspector _inspector =
        inspector ?? new NoopContainerHostInspector();

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
                _inspector));
}

public sealed class ContainerHostInspectOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    IContainerHostInspector inspector) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly IContainerHostInspector _inspector = inspector;

    public Resource Resource => Context.Resource;

    public ResourceOperationResolution Definition { get; } = operation;

    public ResourceOperationId OperationId => ContainerHostResourceTypeProvider.Operations.Inspect;

    public bool IsAvailable => Definition.IsAvailable;

    public string? UnavailableReason => Definition.UnavailableReason;

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

        var diagnostics = await _inspector.InspectAsync(
            Resource,
            cancellationToken);

        return new ResourceOperationExecutionResult(
            Resource,
            OperationId,
            diagnostics);
    }
}

public sealed record ContainerHostInspectionPlan(
    Resource Resource,
    string? HostKind,
    string? Endpoint);
