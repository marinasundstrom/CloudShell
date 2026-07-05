namespace CloudShell.ControlPlane.Providers;

public sealed class PythonAppStartOperationProvider :
    PythonAppLifecycleOperationProvider
{
    public PythonAppStartOperationProvider(
        IPythonAppRuntimeController? runtimeController = null)
        : base(
            PythonAppResourceTypeProvider.Operations.Start,
            runtimeController)
    {
    }
}

public sealed class PythonAppRestartOperationProvider :
    PythonAppLifecycleOperationProvider
{
    public PythonAppRestartOperationProvider(
        IPythonAppRuntimeController? runtimeController = null)
        : base(
            PythonAppResourceTypeProvider.Operations.Restart,
            runtimeController)
    {
    }
}

public sealed class PythonAppStopOperationProvider :
    PythonAppLifecycleOperationProvider
{
    public PythonAppStopOperationProvider(
        IPythonAppRuntimeController? runtimeController = null)
        : base(
            PythonAppResourceTypeProvider.Operations.Stop,
            runtimeController)
    {
    }
}

public abstract class PythonAppLifecycleOperationProvider(
    ResourceOperationId operationId,
    IPythonAppRuntimeController? runtimeController = null) :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly IPythonAppRuntimeController _runtimeController =
        runtimeController ?? new NoopPythonAppRuntimeController();

    public ResourceOperationId OperationId { get; } = operationId;

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
                _runtimeController));
}

public sealed class PythonAppLifecycleOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    IPythonAppRuntimeController runtimeController) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly IPythonAppRuntimeController _runtimeController =
        runtimeController;

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
                        "application.pythonApp.operationUnavailable",
                        UnavailableReason ?? $"The '{OperationId}' operation is not available.",
                        OperationId)
                ]);
        }

        var diagnostics = await _runtimeController.ExecuteAsync(
            Resource,
            OperationId,
            cancellationToken);

        return new ResourceOperationExecutionResult(
            Resource,
            OperationId,
            diagnostics);
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
}
