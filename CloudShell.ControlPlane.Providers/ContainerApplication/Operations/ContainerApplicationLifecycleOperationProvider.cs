namespace CloudShell.ControlPlane.Providers;

public sealed class ContainerApplicationStartOperationProvider :
    ContainerApplicationLifecycleOperationProvider
{
    public ContainerApplicationStartOperationProvider(
        IContainerApplicationRuntimeHandler? runtimeHandler = null,
        IProviderExecutionDispatcher? dispatcher = null)
        : base(
            ContainerApplicationResourceTypeProvider.Operations.Start,
            runtimeHandler,
            dispatcher)
    {
    }
}

public sealed class ContainerApplicationRestartOperationProvider :
    ContainerApplicationLifecycleOperationProvider
{
    public ContainerApplicationRestartOperationProvider(
        IContainerApplicationRuntimeHandler? runtimeHandler = null,
        IProviderExecutionDispatcher? dispatcher = null)
        : base(
            ContainerApplicationResourceTypeProvider.Operations.Restart,
            runtimeHandler,
            dispatcher)
    {
    }
}

public sealed class ContainerApplicationStopOperationProvider :
    ContainerApplicationLifecycleOperationProvider
{
    public ContainerApplicationStopOperationProvider(
        IContainerApplicationRuntimeHandler? runtimeHandler = null,
        IProviderExecutionDispatcher? dispatcher = null)
        : base(
            ContainerApplicationResourceTypeProvider.Operations.Stop,
            runtimeHandler,
            dispatcher)
    {
    }
}

public abstract class ContainerApplicationLifecycleOperationProvider(
    ResourceOperationId operationId,
    IContainerApplicationRuntimeHandler? runtimeHandler = null,
    IProviderExecutionDispatcher? dispatcher = null) :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly IProviderExecutionDispatcher _dispatcher =
        dispatcher ?? CreateDefaultDispatcher(
            runtimeHandler ?? new NoopContainerApplicationRuntimeHandler());

    public ResourceOperationId OperationId { get; } = operationId;

    public ResourceDefinitionValueSource ResolutionLevel =>
        ResourceDefinitionValueSource.TypeDefinition;

    public bool CanHandle(
        Resource resource,
        ResourceOperationResolution operation) =>
        resource.Type.TypeId == ContainerApplicationResourceTypeProvider.ResourceTypeId &&
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
            new ContainerApplicationLifecycleOperation(
                context.ExecutionContext ?? new ResourceProjectionExecutionContext(resource),
                operation,
                _dispatcher));

    private static IProviderExecutionDispatcher CreateDefaultDispatcher(
        IContainerApplicationRuntimeHandler runtimeHandler) =>
        new InProcessProviderExecutionDispatcher(
            [
                new ContainerApplicationStartExecutionHandler(runtimeHandler),
                new ContainerApplicationStopExecutionHandler(runtimeHandler),
                new ContainerApplicationRestartExecutionHandler(runtimeHandler)
            ]);
}

public sealed class ContainerApplicationLifecycleOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
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
        ValueTask.FromResult(IsAvailable);

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
                        "application.container.operationUnavailable",
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

    private static string GetInstructionType(ResourceOperationId operationId)
    {
        if (operationId == ContainerApplicationResourceTypeProvider.Operations.Start)
        {
            return ProviderExecutionInstructionTypes.ContainerApplicationStart;
        }

        if (operationId == ContainerApplicationResourceTypeProvider.Operations.Stop)
        {
            return ProviderExecutionInstructionTypes.ContainerApplicationStop;
        }

        if (operationId == ContainerApplicationResourceTypeProvider.Operations.Restart)
        {
            return ProviderExecutionInstructionTypes.ContainerApplicationRestart;
        }

        return operationId.Value;
    }
}
