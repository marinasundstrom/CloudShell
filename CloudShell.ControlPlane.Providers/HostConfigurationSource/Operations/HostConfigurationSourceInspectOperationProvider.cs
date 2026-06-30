namespace CloudShell.ControlPlane.Providers;

public sealed class HostConfigurationSourceInspectOperationProvider(
    IHostConfigurationSourceInspector? inspector = null) :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly IHostConfigurationSourceInspector _inspector =
        inspector ?? new NoopHostConfigurationSourceInspector();

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
                _inspector));
}

public sealed class HostConfigurationSourceInspectOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    IHostConfigurationSourceInspector inspector) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly IHostConfigurationSourceInspector _inspector = inspector;

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

        var diagnostics = await _inspector.InspectAsync(
            Resource,
            cancellationToken);

        return new ResourceOperationExecutionResult(
            Resource,
            OperationId,
            diagnostics);
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
