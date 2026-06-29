namespace CloudShell.ControlPlane.Providers;

public sealed class DockerHostInspectOperationProvider(
    IDockerHostInspector? inspector = null) :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly IDockerHostInspector _inspector =
        inspector ?? new NoopDockerHostInspector();

    public ResourceOperationId OperationId =>
        DockerHostResourceTypeProvider.Operations.Inspect;

    public ResourceDefinitionValueSource ResolutionLevel =>
        ResourceDefinitionValueSource.TypeDefinition;

    public bool CanHandle(
        Resource resource,
        ResourceOperationResolution operation) =>
        resource.Type.TypeId == DockerHostResourceTypeProvider.ResourceTypeId &&
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
            new DockerHostInspectOperation(
                context.ExecutionContext ?? new ResourceProjectionExecutionContext(resource),
                operation,
                _inspector));
}

public sealed class DockerHostInspectOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    IDockerHostInspector inspector) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly IDockerHostInspector _inspector = inspector;

    public Resource Resource => Context.Resource;

    public ResourceOperationResolution Definition { get; } = operation;

    public ResourceOperationId OperationId => DockerHostResourceTypeProvider.Operations.Inspect;

    public bool IsAvailable => Definition.IsAvailable;

    public string? UnavailableReason => Definition.UnavailableReason;

    public ValueTask<bool> CanExecuteAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(IsAvailable);

    public DockerHostInspectionPlan PlanInspection() =>
        new(
            Resource,
            Resource.Attributes.GetString(DockerHostResourceTypeProvider.Attributes.HostKind),
            Resource.Attributes.GetString(DockerHostResourceTypeProvider.Attributes.Endpoint),
            Resource.Attributes.GetString(DockerHostResourceTypeProvider.Attributes.Registry));

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
                        "docker.host.inspectUnavailable",
                        UnavailableReason ?? "The Docker host inspect operation is not available.",
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
}

public sealed record DockerHostInspectionPlan(
    Resource Resource,
    string? HostKind,
    string? Endpoint,
    string? Registry);
