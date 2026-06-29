namespace CloudShell.ResourceModel.ReferenceProviders;

public sealed class DockerContainerStartOperationProvider :
    DockerContainerLifecycleOperationProvider
{
    public DockerContainerStartOperationProvider(
        IDockerContainerRuntimeHandler? runtimeHandler = null)
        : base(
            DockerContainerResourceTypeProvider.Operations.Start,
            runtimeHandler)
    {
    }
}

public sealed class DockerContainerStopOperationProvider :
    DockerContainerLifecycleOperationProvider
{
    public DockerContainerStopOperationProvider(
        IDockerContainerRuntimeHandler? runtimeHandler = null)
        : base(
            DockerContainerResourceTypeProvider.Operations.Stop,
            runtimeHandler)
    {
    }
}

public sealed class DockerContainerPauseOperationProvider :
    DockerContainerLifecycleOperationProvider
{
    public DockerContainerPauseOperationProvider(
        IDockerContainerRuntimeHandler? runtimeHandler = null)
        : base(
            DockerContainerResourceTypeProvider.Operations.Pause,
            runtimeHandler)
    {
    }
}

public sealed class DockerContainerRestartOperationProvider :
    DockerContainerLifecycleOperationProvider
{
    public DockerContainerRestartOperationProvider(
        IDockerContainerRuntimeHandler? runtimeHandler = null)
        : base(
            DockerContainerResourceTypeProvider.Operations.Restart,
            runtimeHandler)
    {
    }
}

public sealed class DockerContainerUnpauseOperationProvider :
    DockerContainerLifecycleOperationProvider
{
    public DockerContainerUnpauseOperationProvider(
        IDockerContainerRuntimeHandler? runtimeHandler = null)
        : base(
            DockerContainerResourceTypeProvider.Operations.Unpause,
            runtimeHandler)
    {
    }
}

public abstract class DockerContainerLifecycleOperationProvider(
    ResourceOperationId operationId,
    IDockerContainerRuntimeHandler? runtimeHandler = null) :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly IDockerContainerRuntimeHandler _runtimeHandler =
        runtimeHandler ?? new NoopDockerContainerRuntimeHandler();

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
                _runtimeHandler));
}

public sealed class DockerContainerLifecycleOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    IDockerContainerRuntimeHandler runtimeHandler) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly IDockerContainerRuntimeHandler _runtimeHandler = runtimeHandler;

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

        var diagnostics = await _runtimeHandler.ExecuteLifecycleAsync(
            Resource,
            OperationId,
            cancellationToken);

        return new ResourceOperationExecutionResult(
            Resource,
            OperationId,
            diagnostics);
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
}
