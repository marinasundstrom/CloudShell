namespace CloudShell.ControlPlane.Providers;

public sealed class IdentityProvisioningSetupOperationProvider(
    IIdentityProvisioningSetupHandler? setupHandler = null) :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly IIdentityProvisioningSetupHandler _setupHandler =
        setupHandler ?? new NoopIdentityProvisioningSetupHandler();

    public ResourceOperationId OperationId =>
        IdentityProvisioningResourceTypeProvider.Operations.Setup;

    public ResourceDefinitionValueSource ResolutionLevel =>
        ResourceDefinitionValueSource.TypeDefinition;

    public bool CanHandle(
        Resource resource,
        ResourceOperationResolution operation) =>
        resource.Type.TypeId == IdentityProvisioningResourceTypeProvider.ResourceTypeId &&
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
            new IdentityProvisioningSetupOperation(
                context.ExecutionContext ?? new ResourceProjectionExecutionContext(resource),
                operation,
                _setupHandler));
}

public sealed class IdentityProvisioningSetupOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    IIdentityProvisioningSetupHandler setupHandler) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly IIdentityProvisioningSetupHandler _setupHandler = setupHandler;

    public Resource Resource => Context.Resource;

    public ResourceOperationResolution Definition { get; } = operation;

    public ResourceOperationId OperationId =>
        IdentityProvisioningResourceTypeProvider.Operations.Setup;

    public bool IsAvailable => Definition.IsAvailable;

    public string? UnavailableReason => Definition.UnavailableReason;

    public ValueTask<bool> CanExecuteAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(IsAvailable);

    public IdentityProvisioningSetupPlan PlanSetup() =>
        new(
            Resource,
            Resource.Attributes.GetString(IdentityProvisioningResourceTypeProvider.Attributes.IdentityProvider),
            Resource.Attributes.GetString(IdentityProvisioningResourceTypeProvider.Attributes.ProviderKind));

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
                        "identity.provisioning.setupUnavailable",
                        UnavailableReason ?? "The identity provisioning setup operation is not available.",
                        OperationId)
                ]);
        }

        var diagnostics = await _setupHandler.SetupAsync(
            Resource,
            cancellationToken);

        return new ResourceOperationExecutionResult(
            Resource,
            OperationId,
            diagnostics);
    }
}

public sealed record IdentityProvisioningSetupPlan(
    Resource Resource,
    string? IdentityProvider,
    string? ProviderKind);
