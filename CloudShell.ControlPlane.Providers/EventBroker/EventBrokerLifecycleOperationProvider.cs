namespace CloudShell.ControlPlane.Providers;

public sealed class EventBrokerStartOperationProvider(
    IEventBrokerRuntimeController? runtimeController = null,
    IProviderExecutionDispatcher? dispatcher = null) :
    EventBrokerLifecycleOperationProvider(
        EventBrokerResourceTypeProvider.Operations.Start,
        runtimeController,
        dispatcher);

public sealed class EventBrokerStopOperationProvider(
    IEventBrokerRuntimeController? runtimeController = null,
    IProviderExecutionDispatcher? dispatcher = null) :
    EventBrokerLifecycleOperationProvider(
        EventBrokerResourceTypeProvider.Operations.Stop,
        runtimeController,
        dispatcher);

public sealed class EventBrokerRestartOperationProvider(
    IEventBrokerRuntimeController? runtimeController = null,
    IProviderExecutionDispatcher? dispatcher = null) :
    EventBrokerLifecycleOperationProvider(
        EventBrokerResourceTypeProvider.Operations.Restart,
        runtimeController,
        dispatcher);

public abstract class EventBrokerLifecycleOperationProvider(
    ResourceOperationId operationId,
    IEventBrokerRuntimeController? runtimeController = null,
    IProviderExecutionDispatcher? dispatcher = null) :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly IEventBrokerRuntimeController _runtimeController =
        runtimeController ?? new NoopEventBrokerRuntimeController();

    private readonly IProviderExecutionDispatcher _dispatcher =
        dispatcher ?? CreateDefaultDispatcher(
            runtimeController ?? new NoopEventBrokerRuntimeController());

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
                _runtimeController,
                _dispatcher));

    private static IProviderExecutionDispatcher CreateDefaultDispatcher(
        IEventBrokerRuntimeController runtimeController) =>
        new InProcessProviderExecutionDispatcher(
            [
                new EventBrokerStartExecutionHandler(runtimeController),
                new EventBrokerStopExecutionHandler(runtimeController),
                new EventBrokerRestartExecutionHandler(runtimeController)
            ]);
}

public sealed class EventBrokerLifecycleOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    IEventBrokerRuntimeController runtimeController,
    IProviderExecutionDispatcher dispatcher) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly IProviderExecutionDispatcher _dispatcher = dispatcher;

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

        var result = await _dispatcher.ExecuteAsync(
            new ProviderExecutionRequest
            {
                AssignmentId = $"{Resource.EffectiveResourceId}:{OperationId}",
                InstructionType = GetInstructionType(OperationId),
                TargetResourceId = Resource.EffectiveResourceId,
                DesiredGeneration = Resource.Revision.Value,
                IdempotencyKey = $"{Resource.EffectiveResourceId}:{OperationId}:{Resource.Revision.Value}",
                RequiredCapabilities = [ProviderExecutionCapabilities.Processes],
                TargetResourceSnapshot = Resource,
                ResourceSnapshot = Context.Resources,
                RequestedAt = DateTimeOffset.UtcNow
            },
            cancellationToken);

        return new ResourceOperationExecutionResult(
            Resource,
            OperationId,
            result.Diagnostics);
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

    private static string GetInstructionType(ResourceOperationId operationId)
    {
        if (operationId == EventBrokerResourceTypeProvider.Operations.Start)
        {
            return ProviderExecutionInstructionTypes.EventBrokerStart;
        }

        if (operationId == EventBrokerResourceTypeProvider.Operations.Stop)
        {
            return ProviderExecutionInstructionTypes.EventBrokerStop;
        }

        if (operationId == EventBrokerResourceTypeProvider.Operations.Restart)
        {
            return ProviderExecutionInstructionTypes.EventBrokerRestart;
        }

        return operationId.Value;
    }
}
