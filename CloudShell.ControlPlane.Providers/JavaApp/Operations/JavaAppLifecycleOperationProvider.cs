namespace CloudShell.ControlPlane.Providers;

public sealed class JavaAppStartOperationProvider :
    JavaAppLifecycleOperationProvider
{
    public JavaAppStartOperationProvider(
        IJavaAppRuntimeController? runtimeController = null,
        IProviderExecutionDispatcher? dispatcher = null)
        : base(
            JavaAppResourceTypeProvider.Operations.Start,
            runtimeController,
            dispatcher)
    {
    }
}

public sealed class JavaAppRestartOperationProvider :
    JavaAppLifecycleOperationProvider
{
    public JavaAppRestartOperationProvider(
        IJavaAppRuntimeController? runtimeController = null,
        IProviderExecutionDispatcher? dispatcher = null)
        : base(
            JavaAppResourceTypeProvider.Operations.Restart,
            runtimeController,
            dispatcher)
    {
    }
}

public sealed class JavaAppStopOperationProvider :
    JavaAppLifecycleOperationProvider
{
    public JavaAppStopOperationProvider(
        IJavaAppRuntimeController? runtimeController = null,
        IProviderExecutionDispatcher? dispatcher = null)
        : base(
            JavaAppResourceTypeProvider.Operations.Stop,
            runtimeController,
            dispatcher)
    {
    }
}

public abstract class JavaAppLifecycleOperationProvider :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly IJavaAppRuntimeController _runtimeController;
    private readonly IProviderExecutionDispatcher _dispatcher;

    protected JavaAppLifecycleOperationProvider(
        ResourceOperationId operationId,
        IJavaAppRuntimeController? runtimeController = null,
        IProviderExecutionDispatcher? dispatcher = null)
    {
        OperationId = operationId;
        _runtimeController = runtimeController ?? new NoopJavaAppRuntimeController();
        _dispatcher = dispatcher ?? new InProcessProviderExecutionDispatcher(
            [
                new JavaAppStartExecutionHandler(_runtimeController),
                new JavaAppStopExecutionHandler(_runtimeController),
                new JavaAppRestartExecutionHandler(_runtimeController)
            ]);
    }

    public ResourceOperationId OperationId { get; }

    public ResourceDefinitionValueSource ResolutionLevel =>
        ResourceDefinitionValueSource.TypeDefinition;

    public bool CanHandle(
        Resource resource,
        ResourceOperationResolution operation) =>
        resource.Type.TypeId == JavaAppResourceTypeProvider.ResourceTypeId &&
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
            new JavaAppLifecycleOperation(
                context.ExecutionContext ?? new ResourceProjectionExecutionContext(resource),
                operation,
                _runtimeController,
                _dispatcher,
                GetUnavailableReason(resource)));

    private string? GetUnavailableReason(Resource resource) =>
        JavaAppRuntimeReadiness.IsMissing(_runtimeController)
            ? JavaAppRuntimeReadiness.CreateMissingReason(resource, OperationId)
            : null;
}

public sealed class JavaAppLifecycleOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    IJavaAppRuntimeController runtimeController,
    IProviderExecutionDispatcher dispatcher,
    string? unavailableReason = null) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly IJavaAppRuntimeController _runtimeController =
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
                        "application.javaApp.operationUnavailable",
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

    private bool CanExecuteForStatus(JavaAppRuntimeStatus status) =>
        status switch
        {
            JavaAppRuntimeStatus.Running =>
                OperationId == JavaAppResourceTypeProvider.Operations.Stop ||
                OperationId == JavaAppResourceTypeProvider.Operations.Restart,
            JavaAppRuntimeStatus.Stopped =>
                OperationId == JavaAppResourceTypeProvider.Operations.Start,
            _ => true
        };

    private static string GetInstructionType(ResourceOperationId operationId)
    {
        if (operationId == JavaAppResourceTypeProvider.Operations.Start)
        {
            return ProviderExecutionInstructionTypes.JavaAppStart;
        }

        if (operationId == JavaAppResourceTypeProvider.Operations.Stop)
        {
            return ProviderExecutionInstructionTypes.JavaAppStop;
        }

        if (operationId == JavaAppResourceTypeProvider.Operations.Restart)
        {
            return ProviderExecutionInstructionTypes.JavaAppRestart;
        }

        throw new InvalidOperationException(
            $"Java app lifecycle operation '{operationId}' does not have a provider execution instruction.");
    }
}
