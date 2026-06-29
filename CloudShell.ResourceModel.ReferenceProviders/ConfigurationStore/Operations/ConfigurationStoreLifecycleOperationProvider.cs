namespace CloudShell.ResourceModel.ReferenceProviders;

public sealed class ConfigurationStoreStartOperationProvider(
    IConfigurationStoreRuntimeController? runtimeController = null) :
    ConfigurationStoreLifecycleOperationProvider(
        ConfigurationStoreResourceTypeProvider.Operations.Start,
        runtimeController);

public sealed class ConfigurationStoreStopOperationProvider(
    IConfigurationStoreRuntimeController? runtimeController = null) :
    ConfigurationStoreLifecycleOperationProvider(
        ConfigurationStoreResourceTypeProvider.Operations.Stop,
        runtimeController);

public sealed class ConfigurationStoreRestartOperationProvider(
    IConfigurationStoreRuntimeController? runtimeController = null) :
    ConfigurationStoreLifecycleOperationProvider(
        ConfigurationStoreResourceTypeProvider.Operations.Restart,
        runtimeController);

public abstract class ConfigurationStoreLifecycleOperationProvider(
    ResourceOperationId operationId,
    IConfigurationStoreRuntimeController? runtimeController = null) :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly IConfigurationStoreRuntimeController _runtimeController =
        runtimeController ?? new NoopConfigurationStoreRuntimeController();

    public ResourceOperationId OperationId { get; } = operationId;

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
            new ConfigurationStoreLifecycleOperation(
                context.ExecutionContext ?? new ResourceProjectionExecutionContext(resource),
                operation,
                _runtimeController));
}

public sealed class ConfigurationStoreLifecycleOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    IConfigurationStoreRuntimeController runtimeController) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    public Resource Resource => Context.Resource;

    public ResourceOperationResolution Definition { get; } = operation;

    public ResourceOperationId OperationId => Definition.Id;

    public bool IsAvailable => Definition.IsAvailable;

    public string? UnavailableReason => Definition.UnavailableReason;

    public ValueTask<bool> CanExecuteAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(IsAvailable && CanExecuteForStatus(
            runtimeController.GetStatus(Resource)));

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
                        "configuration.store.operationUnavailable",
                        UnavailableReason ?? $"The '{OperationId}' operation is not available.",
                        OperationId)
                ]);
        }

        var diagnostics = await runtimeController.ExecuteAsync(
            Resource,
            OperationId,
            cancellationToken);

        return new ResourceOperationExecutionResult(
            Resource,
            OperationId,
            diagnostics);
    }

    private bool CanExecuteForStatus(ResourceWebAppRuntimeStatus status) =>
        status switch
        {
            ResourceWebAppRuntimeStatus.Running =>
                OperationId == ConfigurationStoreResourceTypeProvider.Operations.Stop ||
                OperationId == ConfigurationStoreResourceTypeProvider.Operations.Restart,
            ResourceWebAppRuntimeStatus.Stopped =>
                OperationId == ConfigurationStoreResourceTypeProvider.Operations.Start,
            _ => true
        };
}
