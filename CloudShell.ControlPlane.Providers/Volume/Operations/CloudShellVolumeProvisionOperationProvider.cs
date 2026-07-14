namespace CloudShell.ControlPlane.Providers;

public sealed class CloudShellVolumeProvisionOperationProvider(
    ICloudShellVolumeProvisioner? provisioner = null,
    IProviderExecutionDispatcher? dispatcher = null) :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly IProviderExecutionDispatcher _dispatcher =
        dispatcher ?? new InProcessProviderExecutionDispatcher(
            [new CloudShellVolumeProvisionExecutionHandler(provisioner)]);

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
                _dispatcher));
}

public sealed class CloudShellVolumeProvisionOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    IProviderExecutionDispatcher dispatcher) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly IProviderExecutionDispatcher _dispatcher = dispatcher;

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

        var result = await _dispatcher.ExecuteAsync(
            new ProviderExecutionRequest
            {
                AssignmentId = $"{Resource.EffectiveResourceId}:{OperationId}",
                InstructionType = ProviderExecutionInstructionTypes.StorageVolumeProvision,
                TargetResourceId = Resource.EffectiveResourceId,
                DesiredGeneration = Resource.Revision.Value,
                IdempotencyKey = $"{Resource.EffectiveResourceId}:{OperationId}:{Resource.Revision.Value}",
                RequiredCapabilities = [ProviderExecutionCapabilities.Storage],
                TargetResourceSnapshot = Resource,
                ResourceSnapshot = Context.Resources,
                RequestedAt = DateTimeOffset.UtcNow
            },
            cancellationToken);

        return new ResourceOperationExecutionResult(
            Resource,
            OperationId,
            result.Diagnostics);
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
