namespace CloudShell.ControlPlane.Providers;

public sealed class DeviceRegistryStartOperationProvider(
    IDeviceRegistryRuntimeController? runtimeController = null) :
    DeviceRegistryLifecycleOperationProvider(
        DeviceRegistryResourceTypeProvider.Operations.Start,
        runtimeController);

public sealed class DeviceRegistryStopOperationProvider(
    IDeviceRegistryRuntimeController? runtimeController = null) :
    DeviceRegistryLifecycleOperationProvider(
        DeviceRegistryResourceTypeProvider.Operations.Stop,
        runtimeController);

public sealed class DeviceRegistryRestartOperationProvider(
    IDeviceRegistryRuntimeController? runtimeController = null) :
    DeviceRegistryLifecycleOperationProvider(
        DeviceRegistryResourceTypeProvider.Operations.Restart,
        runtimeController);

public abstract class DeviceRegistryLifecycleOperationProvider(
    ResourceOperationId operationId,
    IDeviceRegistryRuntimeController? runtimeController = null) :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly IDeviceRegistryRuntimeController _runtimeController =
        runtimeController ?? new NoopDeviceRegistryRuntimeController();

    public ResourceOperationId OperationId { get; } = operationId;

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
                _runtimeController));
}

public sealed class DeviceRegistryLifecycleOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    IDeviceRegistryRuntimeController runtimeController) : IResourceOperationExecutorProjection
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
                        "iot.deviceRegistry.operationUnavailable",
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
                OperationId == DeviceRegistryResourceTypeProvider.Operations.Stop ||
                OperationId == DeviceRegistryResourceTypeProvider.Operations.Restart,
            ResourceWebAppRuntimeStatus.Stopped =>
                OperationId == DeviceRegistryResourceTypeProvider.Operations.Start,
            _ => true
        };
}
