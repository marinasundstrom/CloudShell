namespace CloudShell.ControlPlane.Providers;

public sealed class EventBrokerStartOperationProvider(
    IEventBrokerRuntimeController? runtimeController = null) :
    EventBrokerLifecycleOperationProvider(
        EventBrokerResourceTypeProvider.Operations.Start,
        runtimeController);

public sealed class EventBrokerStopOperationProvider(
    IEventBrokerRuntimeController? runtimeController = null) :
    EventBrokerLifecycleOperationProvider(
        EventBrokerResourceTypeProvider.Operations.Stop,
        runtimeController);

public sealed class EventBrokerRestartOperationProvider(
    IEventBrokerRuntimeController? runtimeController = null) :
    EventBrokerLifecycleOperationProvider(
        EventBrokerResourceTypeProvider.Operations.Restart,
        runtimeController);

public abstract class EventBrokerLifecycleOperationProvider(
    ResourceOperationId operationId,
    IEventBrokerRuntimeController? runtimeController = null) :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly IEventBrokerRuntimeController _runtimeController =
        runtimeController ?? new NoopEventBrokerRuntimeController();

    public ResourceOperationId OperationId { get; } = operationId;

    public ResourceDefinitionValueSource ResolutionLevel =>
        ResourceDefinitionValueSource.TypeDefinition;

    public bool CanHandle(
        Resource resource,
        ResourceOperationResolution operation) =>
        resource.Type.TypeId == EventBrokerResourceTypeProvider.ResourceTypeId &&
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
            new EventBrokerLifecycleOperation(
                context.ExecutionContext ?? new ResourceProjectionExecutionContext(resource),
                operation,
                _runtimeController));
}

public sealed class EventBrokerLifecycleOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    IEventBrokerRuntimeController runtimeController) : IResourceOperationExecutorProjection
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
                        "event.broker.operationUnavailable",
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
                OperationId == EventBrokerResourceTypeProvider.Operations.Stop ||
                OperationId == EventBrokerResourceTypeProvider.Operations.Restart,
            ResourceWebAppRuntimeStatus.Stopped =>
                OperationId == EventBrokerResourceTypeProvider.Operations.Start,
            _ => true
        };
}
