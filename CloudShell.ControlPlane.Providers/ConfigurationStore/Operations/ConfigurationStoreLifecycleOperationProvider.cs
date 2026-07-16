namespace CloudShell.ControlPlane.Providers;

public sealed class ConfigurationStoreStartOperationProvider(
    IConfigurationStoreRuntimeController? runtimeController = null,
    IProviderExecutionDispatcher? dispatcher = null) :
    ConfigurationStoreLifecycleOperationProvider(
        ConfigurationStoreResourceTypeProvider.Operations.Start,
        runtimeController,
        dispatcher);

public sealed class ConfigurationStoreStopOperationProvider(
    IConfigurationStoreRuntimeController? runtimeController = null,
    IProviderExecutionDispatcher? dispatcher = null) :
    ConfigurationStoreLifecycleOperationProvider(
        ConfigurationStoreResourceTypeProvider.Operations.Stop,
        runtimeController,
        dispatcher);

public sealed class ConfigurationStoreRestartOperationProvider(
    IConfigurationStoreRuntimeController? runtimeController = null,
    IProviderExecutionDispatcher? dispatcher = null) :
    ConfigurationStoreLifecycleOperationProvider(
        ConfigurationStoreResourceTypeProvider.Operations.Restart,
        runtimeController,
        dispatcher);

public abstract class ConfigurationStoreLifecycleOperationProvider(
    ResourceOperationId operationId,
    IConfigurationStoreRuntimeController? runtimeController = null,
    IProviderExecutionDispatcher? dispatcher = null) :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly IConfigurationStoreRuntimeController _runtimeController =
        runtimeController ?? new NoopConfigurationStoreRuntimeController();

    private readonly IProviderExecutionDispatcher _dispatcher =
        dispatcher ?? CreateDefaultDispatcher(
            runtimeController ?? new NoopConfigurationStoreRuntimeController());

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
                _runtimeController,
                _dispatcher,
                GetUnavailableReason(resource)));

    private string? GetUnavailableReason(Resource resource) =>
        ConfigurationStoreRuntimeReadiness.IsMissing(_runtimeController)
            ? ConfigurationStoreRuntimeReadiness.CreateMissingReason(resource, OperationId)
            : null;

    private static IProviderExecutionDispatcher CreateDefaultDispatcher(
        IConfigurationStoreRuntimeController runtimeController) =>
        new InProcessProviderExecutionDispatcher(
            [
                new ConfigurationStoreStartExecutionHandler(runtimeController),
                new ConfigurationStoreStopExecutionHandler(runtimeController),
                new ConfigurationStoreRestartExecutionHandler(runtimeController)
            ]);
}

public sealed class ConfigurationStoreLifecycleOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    IConfigurationStoreRuntimeController runtimeController,
    IProviderExecutionDispatcher dispatcher,
    string? unavailableReason = null) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly IProviderExecutionDispatcher _dispatcher = dispatcher;

    public Resource Resource => Context.Resource;

    public ResourceOperationResolution Definition { get; } = operation;

    public ResourceOperationId OperationId => Definition.Id;

    public bool IsAvailable =>
        Definition.IsAvailable &&
        string.IsNullOrWhiteSpace(UnavailableReason);

    public string? UnavailableReason { get; } =
        string.IsNullOrWhiteSpace(unavailableReason)
            ? operation.UnavailableReason
            : unavailableReason;

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

        var result = await _dispatcher.ExecuteAsync(
            ProviderExecutionRequests.CreateForResource(
                Resource,
                OperationId.Value,
                GetInstructionType(OperationId),
                [ProviderExecutionCapabilities.Processes],
                Context.Resources),
            cancellationToken);

        return result.ToResourceOperationExecutionResult(Resource, OperationId);
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

    private static string GetInstructionType(ResourceOperationId operationId)
    {
        if (operationId == ConfigurationStoreResourceTypeProvider.Operations.Start)
        {
            return ProviderExecutionInstructionTypes.ConfigurationStoreStart;
        }

        if (operationId == ConfigurationStoreResourceTypeProvider.Operations.Stop)
        {
            return ProviderExecutionInstructionTypes.ConfigurationStoreStop;
        }

        if (operationId == ConfigurationStoreResourceTypeProvider.Operations.Restart)
        {
            return ProviderExecutionInstructionTypes.ConfigurationStoreRestart;
        }

        return operationId.Value;
    }
}
