namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class StorageInspectOperationProvider(
    IStorageInspector? inspector = null) :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly IStorageInspector _inspector =
        inspector ?? new NoopStorageInspector();

    public ResourceOperationId OperationId =>
        StorageResourceTypeProvider.Operations.Inspect;

    public ResourceDefinitionValueSource ResolutionLevel =>
        ResourceDefinitionValueSource.TypeDefinition;

    public bool CanHandle(
        Resource resource,
        ResourceOperationResolution operation) =>
        resource.Type.TypeId == StorageResourceTypeProvider.ResourceTypeId &&
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
            new StorageInspectOperation(
                context.ExecutionContext ?? new ResourceProjectionExecutionContext(resource),
                operation,
                _inspector));
}

public sealed class StorageInspectOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    IStorageInspector inspector) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly IStorageInspector _inspector = inspector;

    public Resource Resource => Context.Resource;

    public ResourceOperationResolution Definition { get; } = operation;

    public ResourceOperationId OperationId => StorageResourceTypeProvider.Operations.Inspect;

    public bool IsAvailable => Definition.IsAvailable;

    public string? UnavailableReason => Definition.UnavailableReason;

    public ValueTask<bool> CanExecuteAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(IsAvailable);

    public StorageInspectionPlan PlanInspection() =>
        new(
            Resource,
            Resource.Attributes.GetString(StorageResourceTypeProvider.Attributes.Provider),
            Resource.Attributes.GetString(StorageResourceTypeProvider.Attributes.Medium),
            Resource.Attributes.GetString(StorageResourceTypeProvider.Attributes.Location));

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
                        "storage.inspectUnavailable",
                        UnavailableReason ?? "The storage inspect operation is not available.",
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

public sealed record StorageInspectionPlan(
    Resource Resource,
    string? Provider,
    string? Medium,
    string? Location);
