namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public interface ICloudShellVolumeProvisioner
{
    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ProvisionAsync(
        Resource resource,
        CancellationToken cancellationToken = default);
}

public sealed class NoopCloudShellVolumeProvisioner :
    ICloudShellVolumeProvisioner
{
    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ProvisionAsync(
        Resource resource,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
}

public sealed class CloudShellVolumeProvisionOperationProvider(
    ICloudShellVolumeProvisioner? provisioner = null) :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly ICloudShellVolumeProvisioner _provisioner =
        provisioner ?? new NoopCloudShellVolumeProvisioner();

    public ResourceOperationId OperationId =>
        CloudShellVolumeResourceTypeProvider.Operations.Provision;

    public ResourceDefinitionValueSource ResolutionLevel =>
        ResourceDefinitionValueSource.TypeDefinition;

    public bool CanHandle(
        Resource resource,
        ResourceOperationResolution operation) =>
        resource.Type.TypeId == CloudShellVolumeResourceTypeProvider.ResourceTypeId &&
        operation.IsAvailable;

    public ValueTask<ResourceDefinitionValidationResult> ValidateAsync(
        Resource resource,
        ResourceOperationResolution operation,
        ResourceProviderContext context,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = CloudShellVolumeProvisionOperation.HasRequiredState(resource)
            ? []
            :
            new[]
            {
                ResourceDefinitionDiagnostic.Error(
                    "storage.volume.provisionUnavailable",
                    "A volume requires a storage medium before it can be provisioned.",
                    CloudShellVolumeResourceTypeProvider.Attributes.StorageMedium)
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
            new CloudShellVolumeProvisionOperation(
                context.ExecutionContext ?? new ResourceProjectionExecutionContext(resource),
                operation,
                _provisioner));
}

public sealed class CloudShellVolumeProvisionOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    ICloudShellVolumeProvisioner provisioner) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly ICloudShellVolumeProvisioner _provisioner = provisioner;

    public Resource Resource => Context.Resource;

    public ResourceOperationResolution Definition { get; } = operation;

    public ResourceOperationId OperationId => CloudShellVolumeResourceTypeProvider.Operations.Provision;

    public bool IsAvailable => Definition.IsAvailable && HasRequiredState(Resource);

    public string? UnavailableReason =>
        Definition.UnavailableReason ??
        (HasRequiredState(Resource) ? null : "A volume requires a storage medium.");

    public ValueTask<bool> CanExecuteAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(IsAvailable);

    public CloudShellVolumeProvisionPlan PlanProvision() =>
        new(
            Resource,
            Resource.Attributes.GetString(CloudShellVolumeResourceTypeProvider.Attributes.Provider),
            Resource.Attributes.GetString(CloudShellVolumeResourceTypeProvider.Attributes.StorageMedium),
            Resource.Attributes.GetString(CloudShellVolumeResourceTypeProvider.Attributes.Location),
            Resource.Attributes.GetString(CloudShellVolumeResourceTypeProvider.Attributes.SubPath),
            Resource.State.StartupDependencies);

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
            CloudShellVolumeResourceTypeProvider.Attributes.StorageMedium));
}

public sealed record CloudShellVolumeProvisionPlan(
    Resource Resource,
    string? Provider,
    string? StorageMedium,
    string? Location,
    string? SubPath,
    IReadOnlyList<ResourceReference> References);
