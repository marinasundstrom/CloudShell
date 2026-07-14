namespace CloudShell.ControlPlane.Providers;

public sealed class DeviceRegistryStartOperationProvider :
    DeviceRegistryLifecycleOperationProvider
{
    public DeviceRegistryStartOperationProvider(
        IDeviceRegistryRuntimeController? runtimeController = null,
        IProviderExecutionDispatcher? dispatcher = null)
        : base(
            DeviceRegistryResourceTypeProvider.Operations.Start,
            runtimeController,
            dispatcher)
    {
    }
}

public sealed class DeviceRegistryStopOperationProvider :
    DeviceRegistryLifecycleOperationProvider
{
    public DeviceRegistryStopOperationProvider(
        IDeviceRegistryRuntimeController? runtimeController = null,
        IProviderExecutionDispatcher? dispatcher = null)
        : base(
            DeviceRegistryResourceTypeProvider.Operations.Stop,
            runtimeController,
            dispatcher)
    {
    }
}

public sealed class DeviceRegistryRestartOperationProvider :
    DeviceRegistryLifecycleOperationProvider
{
    public DeviceRegistryRestartOperationProvider(
        IDeviceRegistryRuntimeController? runtimeController = null,
        IProviderExecutionDispatcher? dispatcher = null)
        : base(
            DeviceRegistryResourceTypeProvider.Operations.Restart,
            runtimeController,
            dispatcher)
    {
    }
}

public abstract class DeviceRegistryLifecycleOperationProvider :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly IDeviceRegistryRuntimeController _runtimeController;
    private readonly IProviderExecutionDispatcher _dispatcher;

    protected DeviceRegistryLifecycleOperationProvider(
        ResourceOperationId operationId,
        IDeviceRegistryRuntimeController? runtimeController = null,
        IProviderExecutionDispatcher? dispatcher = null)
    {
        OperationId = operationId;
        _runtimeController = runtimeController ?? new NoopDeviceRegistryRuntimeController();
        _dispatcher = dispatcher ?? new InProcessProviderExecutionDispatcher(
            [
                new DeviceRegistryStartExecutionHandler(_runtimeController),
                new DeviceRegistryStopExecutionHandler(_runtimeController),
                new DeviceRegistryRestartExecutionHandler(_runtimeController)
            ]);
    }

    public ResourceOperationId OperationId { get; }

    public ResourceDefinitionValueSource ResolutionLevel =>
        ResourceDefinitionValueSource.TypeDefinition;

    public bool CanHandle(
        Resource resource,
        ResourceOperationResolution operation) =>
        resource.Type.TypeId == DeviceRegistryResourceTypeProvider.ResourceTypeId &&
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
            new DeviceRegistryLifecycleOperation(
                context.ExecutionContext ?? new ResourceProjectionExecutionContext(resource),
                operation,
                _runtimeController,
                _dispatcher));
}

public sealed class DeviceRegistryLifecycleOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    IDeviceRegistryRuntimeController runtimeController,
    IProviderExecutionDispatcher dispatcher) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly IDeviceRegistryRuntimeController _runtimeController =
        runtimeController;
    private readonly IProviderExecutionDispatcher _dispatcher = dispatcher;

    public Resource Resource => Context.Resource;

    public ResourceOperationResolution Definition { get; } = operation;

    public ResourceOperationId OperationId => Definition.Id;

    public bool IsAvailable => Definition.IsAvailable;

    public string? UnavailableReason => Definition.UnavailableReason;

    public ValueTask<bool> CanExecuteAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(IsAvailable && CanExecuteForStatus(
            _runtimeController.GetStatus(Resource)));

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
                        "iot.deviceRegistry.operationUnavailable",
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
                OperationId == DeviceRegistryResourceTypeProvider.Operations.Stop ||
                OperationId == DeviceRegistryResourceTypeProvider.Operations.Restart,
            ResourceWebAppRuntimeStatus.Stopped =>
                OperationId == DeviceRegistryResourceTypeProvider.Operations.Start,
            _ => true
        };

    private static string GetInstructionType(ResourceOperationId operationId)
    {
        if (operationId == DeviceRegistryResourceTypeProvider.Operations.Start)
        {
            return ProviderExecutionInstructionTypes.DeviceRegistryStart;
        }

        if (operationId == DeviceRegistryResourceTypeProvider.Operations.Stop)
        {
            return ProviderExecutionInstructionTypes.DeviceRegistryStop;
        }

        if (operationId == DeviceRegistryResourceTypeProvider.Operations.Restart)
        {
            return ProviderExecutionInstructionTypes.DeviceRegistryRestart;
        }

        throw new InvalidOperationException(
            $"Device Registry lifecycle operation '{operationId}' does not have a provider execution instruction.");
    }
}
