namespace CloudShell.ResourceModel.ReferenceProviders;

public sealed class SecretsVaultInspectOperationProvider(
    ISecretsVaultInspector? inspector = null) :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly ISecretsVaultInspector _inspector =
        inspector ?? new NoopSecretsVaultInspector();

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
                _inspector));
}

public sealed class SecretsVaultInspectOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    ISecretsVaultInspector inspector) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly ISecretsVaultInspector _inspector = inspector;

    public Resource Resource => Context.Resource;

    public ResourceOperationResolution Definition { get; } = operation;

    public ResourceOperationId OperationId => SecretsVaultResourceTypeProvider.Operations.Inspect;

    public bool IsAvailable => Definition.IsAvailable;

    public string? UnavailableReason => Definition.UnavailableReason;

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

        var diagnostics = await _inspector.InspectAsync(
            Resource,
            cancellationToken);

        return new ResourceOperationExecutionResult(
            Resource,
            OperationId,
            diagnostics);
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
