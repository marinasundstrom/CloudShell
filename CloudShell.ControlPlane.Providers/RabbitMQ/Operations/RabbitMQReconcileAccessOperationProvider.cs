using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.Providers;

public sealed class RabbitMQReconcileAccessOperationProvider(
    IRabbitMQAccessReconciler? accessReconciler = null,
    IResourcePermissionGrantReader? grantReader = null) :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly IRabbitMQAccessReconciler _accessReconciler =
        accessReconciler ?? new NoopRabbitMQAccessReconciler();
    private readonly IResourcePermissionGrantReader? _grantReader = grantReader;

    public ResourceOperationId OperationId =>
        RabbitMQResourceTypeProvider.Operations.ReconcileAccess;

    public ResourceDefinitionValueSource ResolutionLevel =>
        ResourceDefinitionValueSource.TypeDefinition;

    public bool CanHandle(
        Resource resource,
        ResourceOperationResolution operation) =>
        resource.Type.TypeId == RabbitMQResourceTypeProvider.ResourceTypeId &&
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
            new RabbitMQReconcileAccessOperation(
                context.ExecutionContext ?? new ResourceProjectionExecutionContext(resource),
                operation,
                _accessReconciler,
                _grantReader));
}

public sealed class RabbitMQReconcileAccessOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    IRabbitMQAccessReconciler accessReconciler,
    IResourcePermissionGrantReader? grantReader = null) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly IRabbitMQAccessReconciler _accessReconciler = accessReconciler;

    public Resource Resource => Context.Resource;

    public ResourceOperationResolution Definition { get; } = operation;

    public ResourceOperationId OperationId => RabbitMQResourceTypeProvider.Operations.ReconcileAccess;

    public bool IsAvailable => Definition.IsAvailable;

    public string? UnavailableReason => Definition.UnavailableReason;

    public ValueTask<bool> CanExecuteAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(IsAvailable);

    public RabbitMQAccessReconciliationPlan PlanReconciliation() =>
        new(Resource, GetTargetedRabbitMQGrants());

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
                        "application.rabbitmq.reconcileAccessUnavailable",
                        UnavailableReason ?? "The RabbitMQ access reconciliation operation is not available.",
                        OperationId)
                ]);
        }

        var plan = PlanReconciliation();
        var diagnostics = await _accessReconciler.ReconcileAccessAsync(
            plan.Resource,
            plan.Grants,
            cancellationToken);

        return new ResourceOperationExecutionResult(
            Resource,
            OperationId,
            diagnostics);
    }

    private IReadOnlyList<ResourcePermissionGrant> GetTargetedRabbitMQGrants()
    {
        if (grantReader is null)
        {
            return [];
        }

        return grantReader.GetPermissionGrants()
            .Where(grant =>
                string.Equals(grant.TargetResourceId, Resource.EffectiveResourceId, StringComparison.OrdinalIgnoreCase) &&
                RabbitMQPermissionGrantStatusProvider.IsRabbitMQPermission(grant.Permission))
            .ToArray();
    }
}

public sealed record RabbitMQAccessReconciliationPlan(
    Resource Resource,
    IReadOnlyList<ResourcePermissionGrant> Grants);
