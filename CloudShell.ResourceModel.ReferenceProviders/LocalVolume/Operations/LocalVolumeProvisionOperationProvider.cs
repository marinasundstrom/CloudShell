namespace CloudShell.ResourceModel.ReferenceProviders;

public sealed class LocalVolumeProvisionOperationProvider(
    ILocalVolumeProvisioner? provisioner = null) :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly ILocalVolumeProvisioner _provisioner =
        provisioner ?? new NoopLocalVolumeProvisioner();

    public ResourceOperationId OperationId =>
        LocalVolumeResourceTypeProvider.Operations.Provision;

    public ResourceDefinitionValueSource ResolutionLevel =>
        ResourceDefinitionValueSource.TypeDefinition;

    public bool CanHandle(
        Resource resource,
        ResourceOperationResolution operation) =>
        resource.Type.TypeId == LocalVolumeResourceTypeProvider.ResourceTypeId &&
        operation.IsAvailable;

    public ValueTask<ResourceDefinitionValidationResult> ValidateAsync(
        Resource resource,
        ResourceOperationResolution operation,
        ResourceProviderContext context,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = LocalVolumeProvisionOperation.HasRequiredState(resource)
            ? []
            :
            new[]
            {
                ResourceDefinitionDiagnostic.Error(
                    "storage.volume.provisionUnavailable",
                    "A local volume requires a storage medium before it can be provisioned.",
                    LocalVolumeResourceTypeProvider.Attributes.StorageMedium)
            };

        return ValueTask.FromResult(
            ResourceDefinitionValidationResult.FromDiagnostics(diagnostics));
    }

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
            new LocalVolumeProvisionOperation(
                context.ExecutionContext ?? new ResourceProjectionExecutionContext(resource),
                operation,
                _provisioner));
}

public sealed class LocalVolumeProvisionOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    ILocalVolumeProvisioner provisioner) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly ILocalVolumeProvisioner _provisioner = provisioner;

    public Resource Resource => Context.Resource;

    public ResourceOperationResolution Definition { get; } = operation;

    public ResourceOperationId OperationId => LocalVolumeResourceTypeProvider.Operations.Provision;

    public bool IsAvailable => Definition.IsAvailable && HasRequiredState(Resource);

    public string? UnavailableReason =>
        Definition.UnavailableReason ??
        (HasRequiredState(Resource) ? null : "A local volume requires a storage medium.");

    public ValueTask<bool> CanExecuteAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(IsAvailable);

    public ResourceDefinitionApplyStep PlanProvision() =>
        new(
            Resource.EffectiveResourceId,
            Resource.Type.TypeId,
            ResourceDefinitionApplyStepKind.MaterializeRuntime,
            $"Provision local volume resource '{Resource.Name}'.");

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
                        "storage.volume.provisionUnavailable",
                        UnavailableReason ?? "The provision operation is not available.",
                        OperationId)
                ]);
        }

        var diagnostics = await _provisioner.ProvisionAsync(
            Resource,
            cancellationToken);

        return new ResourceOperationExecutionResult(
            Resource,
            OperationId,
            diagnostics);
    }

    internal static bool HasRequiredState(Resource resource) =>
        !string.IsNullOrWhiteSpace(resource.Attributes.GetString(
            LocalVolumeResourceTypeProvider.Attributes.StorageMedium));
}
