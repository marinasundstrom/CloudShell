using CloudShell.Abstractions.ResourceManager;
using System.Text.Json;

namespace CloudShell.ControlPlane.Providers;

public sealed class RabbitMQReconcileAccessOperationProvider :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly IProviderExecutionDispatcher _dispatcher;
    private readonly IResourcePermissionGrantReader? _grantReader;

    public RabbitMQReconcileAccessOperationProvider(
        IProviderExecutionDispatcher? dispatcher = null,
        IRabbitMQAccessReconciler? accessReconciler = null,
        IResourcePermissionGrantReader? grantReader = null)
    {
        _dispatcher = dispatcher ??
            new InProcessProviderExecutionDispatcher(
                [new RabbitMQAccessReconcileExecutionHandler(accessReconciler)]);
        _grantReader = grantReader;
    }

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
                _dispatcher,
                _grantReader));
}

public sealed class RabbitMQReconcileAccessOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    IProviderExecutionDispatcher dispatcher,
    IResourcePermissionGrantReader? grantReader = null) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly IProviderExecutionDispatcher _dispatcher = dispatcher;

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
        var result = await _dispatcher.ExecuteAsync(
            ProviderExecutionRequests.CreateForResource(
                Resource,
                OperationId.Value,
                ProviderExecutionInstructionTypes.RabbitMQAccessReconcile,
                [ProviderExecutionCapabilities.RabbitMQAccess],
                Context.Resources,
                JsonSerializer.SerializeToElement(
                    new RabbitMQAccessReconcileExecutionPayload(plan.Grants))),
            cancellationToken);

        return new ResourceOperationExecutionResult(
            Resource,
            OperationId,
            result.Diagnostics);
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
