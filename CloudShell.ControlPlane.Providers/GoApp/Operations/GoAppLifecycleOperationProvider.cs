namespace CloudShell.ControlPlane.Providers;

public sealed class GoAppStartOperationProvider :
    GoAppLifecycleOperationProvider
{
    public GoAppStartOperationProvider(
        IGoAppRuntimeController? runtimeController = null)
        : base(
            GoAppResourceTypeProvider.Operations.Start,
            runtimeController)
    {
    }
}

public sealed class GoAppRestartOperationProvider :
    GoAppLifecycleOperationProvider
{
    public GoAppRestartOperationProvider(
        IGoAppRuntimeController? runtimeController = null)
        : base(
            GoAppResourceTypeProvider.Operations.Restart,
            runtimeController)
    {
    }
}

public sealed class GoAppStopOperationProvider :
    GoAppLifecycleOperationProvider
{
    public GoAppStopOperationProvider(
        IGoAppRuntimeController? runtimeController = null)
        : base(
            GoAppResourceTypeProvider.Operations.Stop,
            runtimeController)
    {
    }
}

public abstract class GoAppLifecycleOperationProvider(
    ResourceOperationId operationId,
    IGoAppRuntimeController? runtimeController = null) :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly IGoAppRuntimeController _runtimeController =
        runtimeController ?? new NoopGoAppRuntimeController();

    public ResourceOperationId OperationId { get; } = operationId;

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
                _runtimeController));
}

public sealed class GoAppLifecycleOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    IGoAppRuntimeController runtimeController) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly IGoAppRuntimeController _runtimeController =
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
                        "application.goApp.operationUnavailable",
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
}
