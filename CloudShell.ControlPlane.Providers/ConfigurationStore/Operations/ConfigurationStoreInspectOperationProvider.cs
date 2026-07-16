namespace CloudShell.ControlPlane.Providers;

public sealed class ConfigurationStoreInspectOperationProvider :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly IProviderExecutionDispatcher _dispatcher;
    private readonly IConfigurationStoreInspector? _inspector;

    public ConfigurationStoreInspectOperationProvider(
        IConfigurationStoreInspector? inspector = null,
        IProviderExecutionDispatcher? dispatcher = null)
    {
        _inspector = inspector;
        _dispatcher = dispatcher ?? new InProcessProviderExecutionDispatcher(
            [
                new ConfigurationStoreInspectExecutionHandler(inspector)
            ]);
    }

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
                _dispatcher,
                GetUnavailableReason(resource)));

    private string? GetUnavailableReason(Resource resource) =>
        ConfigurationStoreInspectorReadiness.IsMissing(_inspector)
            ? ConfigurationStoreInspectorReadiness.CreateMissingReason(resource)
            : null;
}

public sealed class ConfigurationStoreInspectOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    IProviderExecutionDispatcher dispatcher,
    string? unavailableReason = null) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly IProviderExecutionDispatcher _dispatcher = dispatcher;

    public Resource Resource => Context.Resource;

    public ResourceOperationResolution Definition { get; } = operation;

    public ResourceOperationId OperationId => ConfigurationStoreResourceTypeProvider.Operations.Inspect;

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

    public ConfigurationStoreInspectionPlan PlanInspection() =>
        new(
            Resource,
            Resource.Attributes.GetString(ConfigurationStoreResourceTypeProvider.Attributes.Endpoint),
            GetSettingCount(Resource));

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

        var result = await _dispatcher.ExecuteAsync(
            ProviderExecutionRequests.CreateForResource(
                Resource,
                OperationId.Value,
                ProviderExecutionInstructionTypes.ConfigurationStoreInspect,
                [ProviderExecutionCapabilities.RuntimeObservation],
                Context.Resources),
            cancellationToken);

        return result.ToResourceOperationExecutionResult(Resource, OperationId);
    }

    private static int GetSettingCount(Resource resource) =>
        int.TryParse(
            resource.Attributes.GetString(ConfigurationStoreResourceTypeProvider.Attributes.SettingCount),
            out var settingCount)
                ? settingCount
                : 0;
}

public sealed record ConfigurationStoreInspectionPlan(
    Resource Resource,
    string? Endpoint,
    int SettingCount);
