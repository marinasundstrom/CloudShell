namespace CloudShell.ControlPlane.Providers;

public sealed class JavaAppStartOperationProvider :
    JavaAppLifecycleOperationProvider
{
    public JavaAppStartOperationProvider(
        IJavaAppRuntimeController? runtimeController = null)
        : base(
            JavaAppResourceTypeProvider.Operations.Start,
            runtimeController)
    {
    }
}

public sealed class JavaAppRestartOperationProvider :
    JavaAppLifecycleOperationProvider
{
    public JavaAppRestartOperationProvider(
        IJavaAppRuntimeController? runtimeController = null)
        : base(
            JavaAppResourceTypeProvider.Operations.Restart,
            runtimeController)
    {
    }
}

public sealed class JavaAppStopOperationProvider :
    JavaAppLifecycleOperationProvider
{
    public JavaAppStopOperationProvider(
        IJavaAppRuntimeController? runtimeController = null)
        : base(
            JavaAppResourceTypeProvider.Operations.Stop,
            runtimeController)
    {
    }
}

public abstract class JavaAppLifecycleOperationProvider(
    ResourceOperationId operationId,
    IJavaAppRuntimeController? runtimeController = null) :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly IJavaAppRuntimeController _runtimeController =
        runtimeController ?? new NoopJavaAppRuntimeController();

    public ResourceOperationId OperationId { get; } = operationId;

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
                _runtimeController));
}

public sealed class JavaAppLifecycleOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    IJavaAppRuntimeController runtimeController) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly IJavaAppRuntimeController _runtimeController =
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
                        "application.javaApp.operationUnavailable",
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
}
