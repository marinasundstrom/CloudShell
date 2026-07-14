namespace CloudShell.ControlPlane.Providers;

public sealed class GoAppStartOperationProvider :
    GoAppLifecycleOperationProvider
{
    public GoAppStartOperationProvider(
        IGoAppRuntimeController? runtimeController = null,
        IProviderExecutionDispatcher? dispatcher = null)
        : base(
            GoAppResourceTypeProvider.Operations.Start,
            runtimeController,
            dispatcher)
    {
    }
}

public sealed class GoAppRestartOperationProvider :
    GoAppLifecycleOperationProvider
{
    public GoAppRestartOperationProvider(
        IGoAppRuntimeController? runtimeController = null,
        IProviderExecutionDispatcher? dispatcher = null)
        : base(
            GoAppResourceTypeProvider.Operations.Restart,
            runtimeController,
            dispatcher)
    {
    }
}

public sealed class GoAppStopOperationProvider :
    GoAppLifecycleOperationProvider
{
    public GoAppStopOperationProvider(
        IGoAppRuntimeController? runtimeController = null,
        IProviderExecutionDispatcher? dispatcher = null)
        : base(
            GoAppResourceTypeProvider.Operations.Stop,
            runtimeController,
            dispatcher)
    {
    }
}

public abstract class GoAppLifecycleOperationProvider :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly IGoAppRuntimeController _runtimeController;
    private readonly IProviderExecutionDispatcher _dispatcher;

    protected GoAppLifecycleOperationProvider(
        ResourceOperationId operationId,
        IGoAppRuntimeController? runtimeController = null,
        IProviderExecutionDispatcher? dispatcher = null)
    {
        OperationId = operationId;
        _runtimeController = runtimeController ?? new NoopGoAppRuntimeController();
        _dispatcher = dispatcher ?? new InProcessProviderExecutionDispatcher(
            [
                new GoAppStartExecutionHandler(_runtimeController),
                new GoAppStopExecutionHandler(_runtimeController),
                new GoAppRestartExecutionHandler(_runtimeController)
            ]);
    }

    public ResourceOperationId OperationId { get; }

    public ResourceDefinitionValueSource ResolutionLevel =>
        ResourceDefinitionValueSource.TypeDefinition;

    public bool CanHandle(
        Resource resource,
        ResourceOperationResolution operation) =>
        resource.Type.TypeId == GoAppResourceTypeProvider.ResourceTypeId &&
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
            new GoAppLifecycleOperation(
                context.ExecutionContext ?? new ResourceProjectionExecutionContext(resource),
                operation,
                _runtimeController,
                _dispatcher));
}

public sealed class GoAppLifecycleOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    IGoAppRuntimeController runtimeController,
    IProviderExecutionDispatcher dispatcher) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly IGoAppRuntimeController _runtimeController =
        runtimeController;
    private readonly IProviderExecutionDispatcher _dispatcher = dispatcher;

    public Resource Resource => Context.Resource;

    public ResourceOperationResolution Definition { get; } = operation;

    public ResourceOperationId OperationId => Definition.Id;

    public bool IsAvailable => Definition.IsAvailable;

    public string? UnavailableReason =>
        Definition.UnavailableReason ??
        ApplicationArtifactResourceValidation.GetLifecycleUnavailableReason(Resource, OperationId);

    public ValueTask<bool> CanExecuteAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(IsAvailable && string.IsNullOrWhiteSpace(UnavailableReason) && CanExecuteForStatus(
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
                        "application.goApp.operationUnavailable",
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

    private bool CanExecuteForStatus(GoAppRuntimeStatus status) =>
        status switch
        {
            GoAppRuntimeStatus.Running =>
                OperationId == GoAppResourceTypeProvider.Operations.Stop ||
                OperationId == GoAppResourceTypeProvider.Operations.Restart,
            GoAppRuntimeStatus.Stopped =>
                OperationId == GoAppResourceTypeProvider.Operations.Start,
            _ => true
        };

    private static string GetInstructionType(ResourceOperationId operationId)
    {
        if (operationId == GoAppResourceTypeProvider.Operations.Start)
        {
            return ProviderExecutionInstructionTypes.GoAppStart;
        }

        if (operationId == GoAppResourceTypeProvider.Operations.Stop)
        {
            return ProviderExecutionInstructionTypes.GoAppStop;
        }

        if (operationId == GoAppResourceTypeProvider.Operations.Restart)
        {
            return ProviderExecutionInstructionTypes.GoAppRestart;
        }

        throw new InvalidOperationException(
            $"Go app lifecycle operation '{operationId}' does not have a provider execution instruction.");
    }
}
