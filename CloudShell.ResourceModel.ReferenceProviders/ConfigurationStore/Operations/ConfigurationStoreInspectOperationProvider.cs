namespace CloudShell.ResourceModel.ReferenceProviders;

public sealed class ConfigurationStoreInspectOperationProvider(
    IConfigurationStoreInspector? inspector = null) :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly IConfigurationStoreInspector _inspector =
        inspector ?? new NoopConfigurationStoreInspector();

    public ResourceOperationId OperationId =>
        ConfigurationStoreResourceTypeProvider.Operations.Inspect;

    public ResourceDefinitionValueSource ResolutionLevel =>
        ResourceDefinitionValueSource.TypeDefinition;

    public bool CanHandle(
        Resource resource,
        ResourceOperationResolution operation) =>
        resource.Type.TypeId == ConfigurationStoreResourceTypeProvider.ResourceTypeId &&
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
            new ConfigurationStoreInspectOperation(
                context.ExecutionContext ?? new ResourceProjectionExecutionContext(resource),
                operation,
                _inspector));
}

public sealed class ConfigurationStoreInspectOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    IConfigurationStoreInspector inspector) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly IConfigurationStoreInspector _inspector = inspector;

    public Resource Resource => Context.Resource;

    public ResourceOperationResolution Definition { get; } = operation;

    public ResourceOperationId OperationId => ConfigurationStoreResourceTypeProvider.Operations.Inspect;

    public bool IsAvailable => Definition.IsAvailable;

    public string? UnavailableReason => Definition.UnavailableReason;

    public ValueTask<bool> CanExecuteAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(IsAvailable);

    public ConfigurationStoreInspectionPlan PlanInspection() =>
        new(
            Resource,
            Resource.Attributes.GetString(ConfigurationStoreResourceTypeProvider.Attributes.Endpoint),
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
                        "configuration.store.inspectUnavailable",
                        UnavailableReason ?? "The configuration store inspect operation is not available.",
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
            resource.Attributes.GetString(ConfigurationStoreResourceTypeProvider.Attributes.EntryCount),
            out var entryCount)
                ? entryCount
                : 0;
}

public sealed record ConfigurationStoreInspectionPlan(
    Resource Resource,
    string? Endpoint,
    int EntryCount);
