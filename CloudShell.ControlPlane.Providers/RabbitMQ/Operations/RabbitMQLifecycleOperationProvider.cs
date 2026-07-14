namespace CloudShell.ControlPlane.Providers;

public sealed class RabbitMQStartOperationProvider :
    RabbitMQLifecycleOperationProvider
{
    public RabbitMQStartOperationProvider(
        IRabbitMQRuntimeHandler? runtimeHandler = null,
        IProviderExecutionDispatcher? dispatcher = null)
        : base(
            RabbitMQResourceTypeProvider.Operations.Start,
            runtimeHandler,
            dispatcher)
    {
    }
}

public sealed class RabbitMQStopOperationProvider :
    RabbitMQLifecycleOperationProvider
{
    public RabbitMQStopOperationProvider(
        IRabbitMQRuntimeHandler? runtimeHandler = null,
        IProviderExecutionDispatcher? dispatcher = null)
        : base(
            RabbitMQResourceTypeProvider.Operations.Stop,
            runtimeHandler,
            dispatcher)
    {
    }
}

public sealed class RabbitMQRestartOperationProvider :
    RabbitMQLifecycleOperationProvider
{
    public RabbitMQRestartOperationProvider(
        IRabbitMQRuntimeHandler? runtimeHandler = null,
        IProviderExecutionDispatcher? dispatcher = null)
        : base(
            RabbitMQResourceTypeProvider.Operations.Restart,
            runtimeHandler,
            dispatcher)
    {
    }
}

public abstract class RabbitMQLifecycleOperationProvider(
    ResourceOperationId operationId,
    IRabbitMQRuntimeHandler? runtimeHandler = null,
    IProviderExecutionDispatcher? dispatcher = null) :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly IRabbitMQRuntimeHandler _runtimeHandler =
        runtimeHandler ?? new NoopRabbitMQRuntimeHandler();

    private readonly IProviderExecutionDispatcher _dispatcher =
        dispatcher ?? CreateDefaultDispatcher(
            runtimeHandler ?? new NoopRabbitMQRuntimeHandler());

    public ResourceOperationId OperationId { get; } = operationId;

    public ResourceDefinitionValueSource ResolutionLevel =>
        ResourceDefinitionValueSource.TypeDefinition;

    public bool CanHandle(
        Resource resource,
        ResourceOperationResolution operation) =>
        resource.Type.TypeId == RabbitMQResourceTypeProvider.ResourceTypeId &&
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
            new RabbitMQLifecycleOperation(
                context.ExecutionContext ?? new ResourceProjectionExecutionContext(resource),
                operation,
                _runtimeHandler,
                _dispatcher));

    private static IProviderExecutionDispatcher CreateDefaultDispatcher(
        IRabbitMQRuntimeHandler runtimeHandler) =>
        new InProcessProviderExecutionDispatcher(
            [
                new RabbitMQStartExecutionHandler(runtimeHandler),
                new RabbitMQStopExecutionHandler(runtimeHandler),
                new RabbitMQRestartExecutionHandler(runtimeHandler)
            ]);
}

public sealed class RabbitMQLifecycleOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    IRabbitMQRuntimeHandler runtimeHandler,
    IProviderExecutionDispatcher dispatcher) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly IRabbitMQRuntimeHandler _runtimeHandler = runtimeHandler;

    private readonly IProviderExecutionDispatcher _dispatcher = dispatcher;

    public Resource Resource => Context.Resource;

    public ResourceOperationResolution Definition { get; } = operation;

    public ResourceOperationId OperationId => Definition.Id;

    public bool IsAvailable => Definition.IsAvailable;

    public string? UnavailableReason => Definition.UnavailableReason;

    public ValueTask<bool> CanExecuteAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(IsAvailable && CanExecuteForStatus(
            _runtimeHandler.GetStatus(Resource)));

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
                        "application.rabbitmq.operationUnavailable",
                        UnavailableReason ?? $"The '{OperationId}' operation is not available.",
                        OperationId)
                ]);
        }

        var result = await _dispatcher.ExecuteAsync(
            ProviderExecutionRequests.CreateForResource(
                Resource,
                OperationId.Value,
                GetInstructionType(OperationId),
                [ProviderExecutionCapabilities.Containers],
                Context.Resources),
            cancellationToken);

        return new ResourceOperationExecutionResult(
            Resource,
            OperationId,
            result.Diagnostics);
    }

    private bool CanExecuteForStatus(RabbitMQRuntimeStatus status) =>
        status switch
        {
            RabbitMQRuntimeStatus.Running =>
                OperationId == RabbitMQResourceTypeProvider.Operations.Stop ||
                OperationId == RabbitMQResourceTypeProvider.Operations.Restart,
            RabbitMQRuntimeStatus.Stopped =>
                OperationId == RabbitMQResourceTypeProvider.Operations.Start,
            _ => true
        };

    private static string GetInstructionType(ResourceOperationId operationId)
    {
        if (operationId == RabbitMQResourceTypeProvider.Operations.Start)
        {
            return ProviderExecutionInstructionTypes.RabbitMQStart;
        }

        if (operationId == RabbitMQResourceTypeProvider.Operations.Stop)
        {
            return ProviderExecutionInstructionTypes.RabbitMQStop;
        }

        if (operationId == RabbitMQResourceTypeProvider.Operations.Restart)
        {
            return ProviderExecutionInstructionTypes.RabbitMQRestart;
        }

        return operationId.Value;
    }
}
