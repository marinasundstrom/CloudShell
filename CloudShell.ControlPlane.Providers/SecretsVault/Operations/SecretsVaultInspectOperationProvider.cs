namespace CloudShell.ControlPlane.Providers;

public sealed class SecretsVaultInspectOperationProvider :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly IProviderExecutionDispatcher _dispatcher;
    private readonly ISecretsVaultInspector? _inspector;

    public SecretsVaultInspectOperationProvider(
        ISecretsVaultInspector? inspector = null,
        IProviderExecutionDispatcher? dispatcher = null)
    {
        _inspector = inspector;
        _dispatcher = dispatcher ?? new InProcessProviderExecutionDispatcher(
            [
                new SecretsVaultInspectExecutionHandler(inspector)
            ]);
    }

    public ResourceOperationId OperationId =>
        SecretsVaultResourceTypeProvider.Operations.Inspect;

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
            new SecretsVaultInspectOperation(
                context.ExecutionContext ?? new ResourceProjectionExecutionContext(resource),
                operation,
                _dispatcher,
                GetUnavailableReason(resource)));

    private string? GetUnavailableReason(Resource resource) =>
        SecretsVaultInspectorReadiness.IsMissing(_inspector)
            ? SecretsVaultInspectorReadiness.CreateMissingReason(resource)
            : null;
}

public sealed class SecretsVaultInspectOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    IProviderExecutionDispatcher dispatcher,
    string? unavailableReason = null) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly IProviderExecutionDispatcher _dispatcher = dispatcher;

    public Resource Resource => Context.Resource;

    public ResourceOperationResolution Definition { get; } = operation;

    public ResourceOperationId OperationId => SecretsVaultResourceTypeProvider.Operations.Inspect;

    public bool IsAvailable =>
        Definition.IsAvailable &&
        string.IsNullOrWhiteSpace(UnavailableReason);

    public string? UnavailableReason { get; } =
        string.IsNullOrWhiteSpace(unavailableReason)
            ? operation.UnavailableReason
            : unavailableReason;

    public ValueTask<bool> CanExecuteAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(IsAvailable);

    public SecretsVaultInspectionPlan PlanInspection() =>
        new(
            Resource,
            Resource.Attributes.GetString(SecretsVaultResourceTypeProvider.Attributes.Endpoint),
            GetSecretCount(Resource));

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
                        "secrets.vault.inspectUnavailable",
                        UnavailableReason ?? "The Secrets Vault inspect operation is not available.",
                        OperationId)
                ]);
        }

        var result = await _dispatcher.ExecuteAsync(
            ProviderExecutionRequests.CreateForResource(
                Resource,
                OperationId.Value,
                ProviderExecutionInstructionTypes.SecretsVaultInspect,
                [ProviderExecutionCapabilities.RuntimeObservation],
                Context.Resources),
            cancellationToken);

        return result.ToResourceOperationExecutionResult(Resource, OperationId);
    }

    private static int GetSecretCount(Resource resource) =>
        int.TryParse(
            resource.Attributes.GetString(SecretsVaultResourceTypeProvider.Attributes.SecretCount),
            out var secretCount)
                ? secretCount
                : 0;
}

public sealed record SecretsVaultInspectionPlan(
    Resource Resource,
    string? Endpoint,
    int SecretCount);
