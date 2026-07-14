namespace CloudShell.ControlPlane.Providers;

public sealed class LoadBalancerApplyConfigurationOperationProvider(
    ILoadBalancerConfigurationApplier? configurationApplier = null,
    IProviderExecutionDispatcher? dispatcher = null) :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly IProviderExecutionDispatcher _dispatcher =
        dispatcher ?? new InProcessProviderExecutionDispatcher(
            [new LoadBalancerConfigurationApplyExecutionHandler(configurationApplier)]);

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
                _dispatcher));
}

public sealed class LoadBalancerApplyConfigurationOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    IProviderExecutionDispatcher dispatcher) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly IProviderExecutionDispatcher _dispatcher = dispatcher;

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

        var result = await _dispatcher.ExecuteAsync(
            new ProviderExecutionRequest
            {
                AssignmentId = $"{Resource.EffectiveResourceId}:{OperationId}",
                InstructionType = ProviderExecutionInstructionTypes.LoadBalancerConfigurationApply,
                TargetResourceId = Resource.EffectiveResourceId,
                DesiredGeneration = Resource.Revision.Value,
                IdempotencyKey = $"{Resource.EffectiveResourceId}:{OperationId}:{Resource.Revision.Value}",
                RequiredCapabilities = [ProviderExecutionCapabilities.LoadBalancing],
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

    private int GetCount(ResourceAttributeId attributeId) =>
        int.TryParse(Resource.Attributes.GetString(attributeId), out var count)
            ? count
            : 0;
}

public sealed record LoadBalancerConfigurationPlan(
    Resource Resource,
    string? Provider,
    int RouteCount);
