namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class LoadBalancerApplyConfigurationOperationProvider(
    ILoadBalancerConfigurationApplier? configurationApplier = null) :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly ILoadBalancerConfigurationApplier _configurationApplier =
        configurationApplier ?? new NoopLoadBalancerConfigurationApplier();

    public ResourceOperationId OperationId =>
        LoadBalancerResourceTypeProvider.Operations.ApplyConfiguration;

    public ResourceDefinitionValueSource ResolutionLevel =>
        ResourceDefinitionValueSource.TypeDefinition;

    public bool CanHandle(
        Resource resource,
        ResourceOperationResolution operation) =>
        resource.Type.TypeId == LoadBalancerResourceTypeProvider.ResourceTypeId &&
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
            new LoadBalancerApplyConfigurationOperation(
                context.ExecutionContext ?? new ResourceProjectionExecutionContext(resource),
                operation,
                _configurationApplier));
}

public sealed class LoadBalancerApplyConfigurationOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    ILoadBalancerConfigurationApplier configurationApplier) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly ILoadBalancerConfigurationApplier _configurationApplier =
        configurationApplier;

    public Resource Resource => Context.Resource;

    public ResourceOperationResolution Definition { get; } = operation;

    public ResourceOperationId OperationId => LoadBalancerResourceTypeProvider.Operations.ApplyConfiguration;

    public bool IsAvailable => Definition.IsAvailable;

    public string? UnavailableReason => Definition.UnavailableReason;

    public ValueTask<bool> CanExecuteAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(IsAvailable);

    public LoadBalancerConfigurationPlan PlanApply() =>
        new(
            Resource,
            Resource.Attributes.GetString(LoadBalancerResourceTypeProvider.Attributes.Provider),
            GetCount(LoadBalancerResourceTypeProvider.Attributes.RouteCount));

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
                        "network.loadBalancer.applyUnavailable",
                        UnavailableReason ?? "The load balancer apply-configuration operation is not available.",
                        OperationId)
                ]);
        }

        var diagnostics = await _configurationApplier.ApplyConfigurationAsync(
            Resource,
            Context,
            cancellationToken);

        return new ResourceOperationExecutionResult(
            Resource,
            OperationId,
            diagnostics);
    }

    private int GetCount(ResourceAttributeId attributeId) =>
        int.TryParse(Resource.Attributes.GetString(attributeId), out var count)
            ? count
            : 0;
}

public sealed record LoadBalancerConfigurationPlan(
    Resource Resource,
    string? Provider,
    int RouteCount);
