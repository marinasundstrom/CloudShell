namespace CloudShell.ControlPlane.Providers;

public sealed class PythonAppStartOperationProvider :
    PythonAppLifecycleOperationProvider
{
    public PythonAppStartOperationProvider(
        IPythonAppRuntimeController? runtimeController = null,
        IProviderExecutionDispatcher? dispatcher = null)
        : base(
            PythonAppResourceTypeProvider.Operations.Start,
            runtimeController,
            dispatcher)
    {
    }
}

public sealed class PythonAppRestartOperationProvider :
    PythonAppLifecycleOperationProvider
{
    public PythonAppRestartOperationProvider(
        IPythonAppRuntimeController? runtimeController = null,
        IProviderExecutionDispatcher? dispatcher = null)
        : base(
            PythonAppResourceTypeProvider.Operations.Restart,
            runtimeController,
            dispatcher)
    {
    }
}

public sealed class PythonAppStopOperationProvider :
    PythonAppLifecycleOperationProvider
{
    public PythonAppStopOperationProvider(
        IPythonAppRuntimeController? runtimeController = null,
        IProviderExecutionDispatcher? dispatcher = null)
        : base(
            PythonAppResourceTypeProvider.Operations.Stop,
            runtimeController,
            dispatcher)
    {
    }
}

public abstract class PythonAppLifecycleOperationProvider :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly IPythonAppRuntimeController _runtimeController;
    private readonly IProviderExecutionDispatcher _dispatcher;

    protected PythonAppLifecycleOperationProvider(
        ResourceOperationId operationId,
        IPythonAppRuntimeController? runtimeController = null,
        IProviderExecutionDispatcher? dispatcher = null)
    {
        OperationId = operationId;
        _runtimeController = runtimeController ?? new NoopPythonAppRuntimeController();
        _dispatcher = dispatcher ?? new InProcessProviderExecutionDispatcher(
            [
                new PythonAppStartExecutionHandler(_runtimeController),
                new PythonAppStopExecutionHandler(_runtimeController),
                new PythonAppRestartExecutionHandler(_runtimeController)
            ]);
    }

    public ResourceOperationId OperationId { get; }

    public ResourceDefinitionValueSource ResolutionLevel =>
        ResourceDefinitionValueSource.TypeDefinition;

    public bool CanHandle(
        Resource resource,
        ResourceOperationResolution operation) =>
        resource.Type.TypeId == PythonAppResourceTypeProvider.ResourceTypeId &&
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
            new PythonAppLifecycleOperation(
                context.ExecutionContext ?? new ResourceProjectionExecutionContext(resource),
                operation,
                _runtimeController,
                _dispatcher,
                GetUnavailableReason(resource)));

    private string? GetUnavailableReason(Resource resource) =>
        PythonAppRuntimeReadiness.IsMissing(_runtimeController)
            ? PythonAppRuntimeReadiness.CreateMissingReason(resource, OperationId)
            : null;
}

public sealed class PythonAppLifecycleOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    IPythonAppRuntimeController runtimeController,
    IProviderExecutionDispatcher dispatcher,
    string? unavailableReason = null) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly IPythonAppRuntimeController _runtimeController =
        runtimeController;
    private readonly IProviderExecutionDispatcher _dispatcher = dispatcher;

    public Resource Resource => Context.Resource;

    public ResourceOperationResolution Definition { get; } = operation;

    public ResourceOperationId OperationId => Definition.Id;

    public bool IsAvailable =>
        Definition.IsAvailable &&
        string.IsNullOrWhiteSpace(UnavailableReason);

    public string? UnavailableReason =>
        unavailableReason ??
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
                        "application.pythonApp.operationUnavailable",
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

    private bool CanExecuteForStatus(PythonAppRuntimeStatus status) =>
        status switch
        {
            PythonAppRuntimeStatus.Running =>
                OperationId == PythonAppResourceTypeProvider.Operations.Stop ||
                OperationId == PythonAppResourceTypeProvider.Operations.Restart,
            PythonAppRuntimeStatus.Stopped =>
                OperationId == PythonAppResourceTypeProvider.Operations.Start,
            _ => true
        };

    private static string GetInstructionType(ResourceOperationId operationId)
    {
        if (operationId == PythonAppResourceTypeProvider.Operations.Start)
        {
            return ProviderExecutionInstructionTypes.PythonAppStart;
        }

        if (operationId == PythonAppResourceTypeProvider.Operations.Stop)
        {
            return ProviderExecutionInstructionTypes.PythonAppStop;
        }

        if (operationId == PythonAppResourceTypeProvider.Operations.Restart)
        {
            return ProviderExecutionInstructionTypes.PythonAppRestart;
        }

        throw new InvalidOperationException(
            $"Python app lifecycle operation '{operationId}' does not have a provider execution instruction.");
    }
}
