namespace CloudShell.ControlPlane.Providers;

public sealed class JavaScriptAppStartOperationProvider :
    JavaScriptAppLifecycleOperationProvider
{
    public JavaScriptAppStartOperationProvider(
        IJavaScriptAppRuntimeController? runtimeController = null)
        : base(
            JavaScriptAppResourceTypeProvider.Operations.Start,
            runtimeController)
    {
    }
}

public sealed class JavaScriptAppRestartOperationProvider :
    JavaScriptAppLifecycleOperationProvider
{
    public JavaScriptAppRestartOperationProvider(
        IJavaScriptAppRuntimeController? runtimeController = null)
        : base(
            JavaScriptAppResourceTypeProvider.Operations.Restart,
            runtimeController)
    {
    }
}

public sealed class JavaScriptAppStopOperationProvider :
    JavaScriptAppLifecycleOperationProvider
{
    public JavaScriptAppStopOperationProvider(
        IJavaScriptAppRuntimeController? runtimeController = null)
        : base(
            JavaScriptAppResourceTypeProvider.Operations.Stop,
            runtimeController)
    {
    }
}

public abstract class JavaScriptAppLifecycleOperationProvider(
    ResourceOperationId operationId,
    IJavaScriptAppRuntimeController? runtimeController = null) :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly IJavaScriptAppRuntimeController _runtimeController =
        runtimeController ?? new NoopJavaScriptAppRuntimeController();

    public ResourceOperationId OperationId { get; } = operationId;

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
                _runtimeController));
}

public sealed class JavaScriptAppLifecycleOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    IJavaScriptAppRuntimeController runtimeController) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly IJavaScriptAppRuntimeController _runtimeController =
        runtimeController;

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

        var diagnostics = await _runtimeController.ExecuteAsync(
            Resource,
            OperationId,
            cancellationToken);

        return new ResourceOperationExecutionResult(
            Resource,
            OperationId,
            diagnostics);
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
}
