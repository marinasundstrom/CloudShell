namespace CloudShell.ControlPlane.Providers;

public sealed class AspNetCoreProjectStartOperationProvider :
    AspNetCoreProjectLifecycleOperationProvider
{
    public AspNetCoreProjectStartOperationProvider(
        IAspNetCoreProjectRuntimeController? runtimeController = null,
        IProviderExecutionDispatcher? dispatcher = null)
        : base(
            AspNetCoreProjectResourceTypeProvider.Operations.Start,
            runtimeController,
            dispatcher)
    {
    }
}

public sealed class AspNetCoreProjectRestartOperationProvider :
    AspNetCoreProjectLifecycleOperationProvider
{
    public AspNetCoreProjectRestartOperationProvider(
        IAspNetCoreProjectRuntimeController? runtimeController = null,
        IProviderExecutionDispatcher? dispatcher = null)
        : base(
            AspNetCoreProjectResourceTypeProvider.Operations.Restart,
            runtimeController,
            dispatcher)
    {
    }
}

public sealed class AspNetCoreProjectStopOperationProvider :
    AspNetCoreProjectLifecycleOperationProvider
{
    public AspNetCoreProjectStopOperationProvider(
        IAspNetCoreProjectRuntimeController? runtimeController = null,
        IProviderExecutionDispatcher? dispatcher = null)
        : base(
            AspNetCoreProjectResourceTypeProvider.Operations.Stop,
            runtimeController,
            dispatcher)
    {
    }
}

public abstract class AspNetCoreProjectLifecycleOperationProvider :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly IAspNetCoreProjectRuntimeController _runtimeController;
    private readonly IProviderExecutionDispatcher _dispatcher;

    protected AspNetCoreProjectLifecycleOperationProvider(
        ResourceOperationId operationId,
        IAspNetCoreProjectRuntimeController? runtimeController = null,
        IProviderExecutionDispatcher? dispatcher = null)
    {
        OperationId = operationId;
        _runtimeController = runtimeController ?? new NoopAspNetCoreProjectRuntimeController();
        _dispatcher = dispatcher ?? new InProcessProviderExecutionDispatcher(
            [
                new AspNetCoreProjectStartExecutionHandler(_runtimeController),
                new AspNetCoreProjectStopExecutionHandler(_runtimeController),
                new AspNetCoreProjectRestartExecutionHandler(_runtimeController)
            ]);
    }

    public ResourceOperationId OperationId { get; }

    public ResourceDefinitionValueSource ResolutionLevel =>
        ResourceDefinitionValueSource.TypeDefinition;

    public bool CanHandle(
        Resource resource,
        ResourceOperationResolution operation) =>
        resource.Type.TypeId == AspNetCoreProjectResourceTypeProvider.ResourceTypeId &&
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
            new AspNetCoreProjectLifecycleOperation(
                context.ExecutionContext ?? new ResourceProjectionExecutionContext(resource),
                operation,
                _runtimeController,
                _dispatcher));
}

public sealed class AspNetCoreProjectLifecycleOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    IAspNetCoreProjectRuntimeController runtimeController,
    IProviderExecutionDispatcher dispatcher) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly IAspNetCoreProjectRuntimeController _runtimeController =
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
                        "application.aspNetCoreProject.operationUnavailable",
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

        return new ResourceOperationExecutionResult(
            Resource,
            OperationId,
            result.Diagnostics);
    }

    private bool CanExecuteForStatus(AspNetCoreProjectRuntimeStatus status) =>
        status switch
        {
            AspNetCoreProjectRuntimeStatus.Running =>
                OperationId == AspNetCoreProjectResourceTypeProvider.Operations.Stop ||
                OperationId == AspNetCoreProjectResourceTypeProvider.Operations.Restart,
            AspNetCoreProjectRuntimeStatus.Stopped =>
                OperationId == AspNetCoreProjectResourceTypeProvider.Operations.Start,
            _ => true
        };

    private static string GetInstructionType(ResourceOperationId operationId)
    {
        if (operationId == AspNetCoreProjectResourceTypeProvider.Operations.Start)
        {
            return ProviderExecutionInstructionTypes.AspNetCoreProjectStart;
        }

        if (operationId == AspNetCoreProjectResourceTypeProvider.Operations.Stop)
        {
            return ProviderExecutionInstructionTypes.AspNetCoreProjectStop;
        }

        if (operationId == AspNetCoreProjectResourceTypeProvider.Operations.Restart)
        {
            return ProviderExecutionInstructionTypes.AspNetCoreProjectRestart;
        }

        throw new InvalidOperationException(
            $"ASP.NET Core project lifecycle operation '{operationId}' does not have a provider execution instruction.");
    }
}
