namespace CloudShell.ControlPlane.Providers;

public sealed class IdentityProvisioningSetupOperationProvider :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly IProviderExecutionDispatcher _dispatcher;

    public IdentityProvisioningSetupOperationProvider(
        IIdentityProvisioningSetupHandler? setupHandler = null,
        IProviderExecutionDispatcher? dispatcher = null)
    {
        var handler = setupHandler ?? new NoopIdentityProvisioningSetupHandler();
        _dispatcher = dispatcher ?? new InProcessProviderExecutionDispatcher(
            [
                new IdentityProvisioningSetupExecutionHandler(handler)
            ]);
    }

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
                _dispatcher));
}

public sealed class IdentityProvisioningSetupOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    IProviderExecutionDispatcher dispatcher) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly IProviderExecutionDispatcher _dispatcher = dispatcher;

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

        var result = await _dispatcher.ExecuteAsync(
            ProviderExecutionRequests.CreateForResource(
                Resource,
                OperationId.Value,
                ProviderExecutionInstructionTypes.IdentityProvisioningSetup,
                [ProviderExecutionCapabilities.IdentityProvisioning],
                Context.Resources),
            cancellationToken);

        return new ResourceOperationExecutionResult(
            Resource,
            OperationId,
            result.Diagnostics);
    }
}

public sealed record IdentityProvisioningSetupPlan(
    Resource Resource,
    string? IdentityProvider,
    string? ProviderKind);
