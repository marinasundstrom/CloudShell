namespace CloudShell.ControlPlane.Providers;

public sealed class JavaScriptAppStartOperationProvider :
    JavaScriptAppLifecycleOperationProvider
{
    public JavaScriptAppStartOperationProvider(
        IJavaScriptAppRuntimeController? runtimeController = null,
        IProviderExecutionDispatcher? dispatcher = null)
        : base(
            JavaScriptAppResourceTypeProvider.Operations.Start,
            runtimeController,
            dispatcher)
    {
    }
}

public sealed class JavaScriptAppRestartOperationProvider :
    JavaScriptAppLifecycleOperationProvider
{
    public JavaScriptAppRestartOperationProvider(
        IJavaScriptAppRuntimeController? runtimeController = null,
        IProviderExecutionDispatcher? dispatcher = null)
        : base(
            JavaScriptAppResourceTypeProvider.Operations.Restart,
            runtimeController,
            dispatcher)
    {
    }
}

public sealed class JavaScriptAppStopOperationProvider :
    JavaScriptAppLifecycleOperationProvider
{
    public JavaScriptAppStopOperationProvider(
        IJavaScriptAppRuntimeController? runtimeController = null,
        IProviderExecutionDispatcher? dispatcher = null)
        : base(
            JavaScriptAppResourceTypeProvider.Operations.Stop,
            runtimeController,
            dispatcher)
    {
    }
}

public abstract class JavaScriptAppLifecycleOperationProvider :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly IJavaScriptAppRuntimeController _runtimeController;
    private readonly IProviderExecutionDispatcher _dispatcher;

    protected JavaScriptAppLifecycleOperationProvider(
        ResourceOperationId operationId,
        IJavaScriptAppRuntimeController? runtimeController = null,
        IProviderExecutionDispatcher? dispatcher = null)
    {
        OperationId = operationId;
        _runtimeController = runtimeController ?? new NoopJavaScriptAppRuntimeController();
        _dispatcher = dispatcher ?? new InProcessProviderExecutionDispatcher(
            [
                new JavaScriptAppStartExecutionHandler(_runtimeController),
                new JavaScriptAppStopExecutionHandler(_runtimeController),
                new JavaScriptAppRestartExecutionHandler(_runtimeController)
            ]);
    }

    public ResourceOperationId OperationId { get; }

    public ResourceDefinitionValueSource ResolutionLevel =>
        ResourceDefinitionValueSource.TypeDefinition;

    public bool CanHandle(
        Resource resource,
        ResourceOperationResolution operation) =>
        resource.Type.TypeId == JavaScriptAppResourceTypeProvider.ResourceTypeId &&
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
            new JavaScriptAppLifecycleOperation(
                context.ExecutionContext ?? new ResourceProjectionExecutionContext(resource),
                operation,
                _runtimeController,
                _dispatcher));
}

public sealed class JavaScriptAppLifecycleOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    IJavaScriptAppRuntimeController runtimeController,
    IProviderExecutionDispatcher dispatcher) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly IJavaScriptAppRuntimeController _runtimeController =
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
                        "application.javascriptApp.operationUnavailable",
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

    private bool CanExecuteForStatus(JavaScriptAppRuntimeStatus status) =>
        status switch
        {
            JavaScriptAppRuntimeStatus.Running =>
                OperationId == JavaScriptAppResourceTypeProvider.Operations.Stop ||
                OperationId == JavaScriptAppResourceTypeProvider.Operations.Restart,
            JavaScriptAppRuntimeStatus.Stopped =>
                OperationId == JavaScriptAppResourceTypeProvider.Operations.Start,
            _ => true
        };

    private static string GetInstructionType(ResourceOperationId operationId)
    {
        if (operationId == JavaScriptAppResourceTypeProvider.Operations.Start)
        {
            return ProviderExecutionInstructionTypes.JavaScriptAppStart;
        }

        if (operationId == JavaScriptAppResourceTypeProvider.Operations.Stop)
        {
            return ProviderExecutionInstructionTypes.JavaScriptAppStop;
        }

        if (operationId == JavaScriptAppResourceTypeProvider.Operations.Restart)
        {
            return ProviderExecutionInstructionTypes.JavaScriptAppRestart;
        }

        throw new InvalidOperationException(
            $"JavaScript app lifecycle operation '{operationId}' does not have a provider execution instruction.");
    }
}
