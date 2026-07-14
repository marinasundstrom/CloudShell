namespace CloudShell.ControlPlane.Providers;

public sealed class SecretsVaultStartOperationProvider(
    ISecretsVaultRuntimeController? runtimeController = null,
    IProviderExecutionDispatcher? dispatcher = null) :
    SecretsVaultLifecycleOperationProvider(
        SecretsVaultResourceTypeProvider.Operations.Start,
        runtimeController,
        dispatcher);

public sealed class SecretsVaultStopOperationProvider(
    ISecretsVaultRuntimeController? runtimeController = null,
    IProviderExecutionDispatcher? dispatcher = null) :
    SecretsVaultLifecycleOperationProvider(
        SecretsVaultResourceTypeProvider.Operations.Stop,
        runtimeController,
        dispatcher);

public sealed class SecretsVaultRestartOperationProvider(
    ISecretsVaultRuntimeController? runtimeController = null,
    IProviderExecutionDispatcher? dispatcher = null) :
    SecretsVaultLifecycleOperationProvider(
        SecretsVaultResourceTypeProvider.Operations.Restart,
        runtimeController,
        dispatcher);

public abstract class SecretsVaultLifecycleOperationProvider(
    ResourceOperationId operationId,
    ISecretsVaultRuntimeController? runtimeController = null,
    IProviderExecutionDispatcher? dispatcher = null) :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly ISecretsVaultRuntimeController _runtimeController =
        runtimeController ?? new NoopSecretsVaultRuntimeController();

    private readonly IProviderExecutionDispatcher _dispatcher =
        dispatcher ?? CreateDefaultDispatcher(
            runtimeController ?? new NoopSecretsVaultRuntimeController());

    public ResourceOperationId OperationId { get; } = operationId;

    public ResourceDefinitionValueSource ResolutionLevel =>
        ResourceDefinitionValueSource.TypeDefinition;

    public bool CanHandle(
        Resource resource,
        ResourceOperationResolution operation) =>
        resource.Type.TypeId == SecretsVaultResourceTypeProvider.ResourceTypeId &&
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
            new SecretsVaultLifecycleOperation(
                context.ExecutionContext ?? new ResourceProjectionExecutionContext(resource),
                operation,
                _runtimeController,
                _dispatcher));

    private static IProviderExecutionDispatcher CreateDefaultDispatcher(
        ISecretsVaultRuntimeController runtimeController) =>
        new InProcessProviderExecutionDispatcher(
            [
                new SecretsVaultStartExecutionHandler(runtimeController),
                new SecretsVaultStopExecutionHandler(runtimeController),
                new SecretsVaultRestartExecutionHandler(runtimeController)
            ]);
}

public sealed class SecretsVaultLifecycleOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    ISecretsVaultRuntimeController runtimeController,
    IProviderExecutionDispatcher dispatcher) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly IProviderExecutionDispatcher _dispatcher = dispatcher;

    public Resource Resource => Context.Resource;

    public ResourceOperationResolution Definition { get; } = operation;

    public ResourceOperationId OperationId => Definition.Id;

    public bool IsAvailable => Definition.IsAvailable;

    public string? UnavailableReason => Definition.UnavailableReason;

    public ValueTask<bool> CanExecuteAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(IsAvailable && CanExecuteForStatus(
            runtimeController.GetStatus(Resource)));

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
                        "secrets.vault.operationUnavailable",
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

    private bool CanExecuteForStatus(ResourceWebAppRuntimeStatus status) =>
        status switch
        {
            ResourceWebAppRuntimeStatus.Running =>
                OperationId == SecretsVaultResourceTypeProvider.Operations.Stop ||
                OperationId == SecretsVaultResourceTypeProvider.Operations.Restart,
            ResourceWebAppRuntimeStatus.Stopped =>
                OperationId == SecretsVaultResourceTypeProvider.Operations.Start,
            _ => true
        };

    private static string GetInstructionType(ResourceOperationId operationId)
    {
        if (operationId == SecretsVaultResourceTypeProvider.Operations.Start)
        {
            return ProviderExecutionInstructionTypes.SecretsVaultStart;
        }

        if (operationId == SecretsVaultResourceTypeProvider.Operations.Stop)
        {
            return ProviderExecutionInstructionTypes.SecretsVaultStop;
        }

        if (operationId == SecretsVaultResourceTypeProvider.Operations.Restart)
        {
            return ProviderExecutionInstructionTypes.SecretsVaultRestart;
        }

        return operationId.Value;
    }
}
