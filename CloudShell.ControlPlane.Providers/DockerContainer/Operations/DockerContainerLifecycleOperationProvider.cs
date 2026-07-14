namespace CloudShell.ControlPlane.Providers;

public sealed class DockerContainerStartOperationProvider :
    DockerContainerLifecycleOperationProvider
{
    public DockerContainerStartOperationProvider(
        IDockerContainerRuntimeHandler? runtimeHandler = null,
        IProviderExecutionDispatcher? dispatcher = null)
        : base(
            DockerContainerResourceTypeProvider.Operations.Start,
            runtimeHandler,
            dispatcher)
    {
    }
}

public sealed class DockerContainerStopOperationProvider :
    DockerContainerLifecycleOperationProvider
{
    public DockerContainerStopOperationProvider(
        IDockerContainerRuntimeHandler? runtimeHandler = null,
        IProviderExecutionDispatcher? dispatcher = null)
        : base(
            DockerContainerResourceTypeProvider.Operations.Stop,
            runtimeHandler,
            dispatcher)
    {
    }
}

public sealed class DockerContainerPauseOperationProvider :
    DockerContainerLifecycleOperationProvider
{
    public DockerContainerPauseOperationProvider(
        IDockerContainerRuntimeHandler? runtimeHandler = null,
        IProviderExecutionDispatcher? dispatcher = null)
        : base(
            DockerContainerResourceTypeProvider.Operations.Pause,
            runtimeHandler,
            dispatcher)
    {
    }
}

public sealed class DockerContainerRestartOperationProvider :
    DockerContainerLifecycleOperationProvider
{
    public DockerContainerRestartOperationProvider(
        IDockerContainerRuntimeHandler? runtimeHandler = null,
        IProviderExecutionDispatcher? dispatcher = null)
        : base(
            DockerContainerResourceTypeProvider.Operations.Restart,
            runtimeHandler,
            dispatcher)
    {
    }
}

public sealed class DockerContainerUnpauseOperationProvider :
    DockerContainerLifecycleOperationProvider
{
    public DockerContainerUnpauseOperationProvider(
        IDockerContainerRuntimeHandler? runtimeHandler = null,
        IProviderExecutionDispatcher? dispatcher = null)
        : base(
            DockerContainerResourceTypeProvider.Operations.Unpause,
            runtimeHandler,
            dispatcher)
    {
    }
}

public abstract class DockerContainerLifecycleOperationProvider(
    ResourceOperationId operationId,
    IDockerContainerRuntimeHandler? runtimeHandler = null,
    IProviderExecutionDispatcher? dispatcher = null) :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly IDockerContainerRuntimeHandler _runtimeHandler =
        runtimeHandler ?? new NoopDockerContainerRuntimeHandler();

    private readonly IProviderExecutionDispatcher _dispatcher =
        dispatcher ?? CreateDefaultDispatcher(
            runtimeHandler ?? new NoopDockerContainerRuntimeHandler());

    public ResourceOperationId OperationId { get; } = operationId;

    public ResourceDefinitionValueSource ResolutionLevel =>
        ResourceDefinitionValueSource.TypeDefinition;

    public bool CanHandle(
        Resource resource,
        ResourceOperationResolution operation) =>
        resource.Type.TypeId == DockerContainerResourceTypeProvider.ResourceTypeId &&
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
            new DockerContainerLifecycleOperation(
                context.ExecutionContext ?? new ResourceProjectionExecutionContext(resource),
                operation,
                _runtimeHandler,
                _dispatcher));

    private static IProviderExecutionDispatcher CreateDefaultDispatcher(
        IDockerContainerRuntimeHandler runtimeHandler) =>
        new InProcessProviderExecutionDispatcher(
            [
                new DockerContainerStartExecutionHandler(runtimeHandler),
                new DockerContainerStopExecutionHandler(runtimeHandler),
                new DockerContainerPauseExecutionHandler(runtimeHandler),
                new DockerContainerRestartExecutionHandler(runtimeHandler),
                new DockerContainerUnpauseExecutionHandler(runtimeHandler)
            ]);
}

public sealed class DockerContainerLifecycleOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    IDockerContainerRuntimeHandler runtimeHandler,
    IProviderExecutionDispatcher dispatcher) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly IDockerContainerRuntimeHandler _runtimeHandler = runtimeHandler;

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
                        "docker.container.operationUnavailable",
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
                RequiredCapabilities = [ProviderExecutionCapabilities.Containers],
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

    private bool CanExecuteForStatus(DockerContainerRuntimeStatus status) =>
        status switch
        {
            DockerContainerRuntimeStatus.Running =>
                OperationId == DockerContainerResourceTypeProvider.Operations.Stop ||
                OperationId == DockerContainerResourceTypeProvider.Operations.Pause ||
                OperationId == DockerContainerResourceTypeProvider.Operations.Restart,
            DockerContainerRuntimeStatus.Paused =>
                OperationId == DockerContainerResourceTypeProvider.Operations.Stop ||
                OperationId == DockerContainerResourceTypeProvider.Operations.Restart ||
                OperationId == DockerContainerResourceTypeProvider.Operations.Unpause,
            DockerContainerRuntimeStatus.Stopped =>
                OperationId == DockerContainerResourceTypeProvider.Operations.Start,
            _ => true
        };

    private static string GetInstructionType(ResourceOperationId operationId)
    {
        if (operationId == DockerContainerResourceTypeProvider.Operations.Start)
        {
            return ProviderExecutionInstructionTypes.ContainerStart;
        }

        if (operationId == DockerContainerResourceTypeProvider.Operations.Stop)
        {
            return ProviderExecutionInstructionTypes.ContainerStop;
        }

        if (operationId == DockerContainerResourceTypeProvider.Operations.Pause)
        {
            return ProviderExecutionInstructionTypes.ContainerPause;
        }

        if (operationId == DockerContainerResourceTypeProvider.Operations.Restart)
        {
            return ProviderExecutionInstructionTypes.ContainerRestart;
        }

        if (operationId == DockerContainerResourceTypeProvider.Operations.Unpause)
        {
            return ProviderExecutionInstructionTypes.ContainerUnpause;
        }

        return operationId.Value;
    }
}
