using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceModel;
using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;

namespace CloudShell.ControlPlane.Providers;

public sealed class ContainerApplicationResourceModelGraphServiceExecutor(
    IContainerApplicationRuntimeHandler? runtimeHandler = null,
    IContainerApplicationOrchestratorRuntimeHandler? orchestratorRuntimeHandler = null,
    IEnumerable<IDeferredContainerApplicationRuntimeSelector>? deferredRuntimeSelectors = null,
    IProviderExecutionDispatcher? dispatcher = null) : IResourceModelGraphOrchestratorServiceExecutor
{
    private readonly IContainerApplicationRuntimeHandler _runtimeHandler =
        runtimeHandler ?? new NoopContainerApplicationRuntimeHandler();
    private readonly IProviderExecutionDispatcher _dispatcher =
        dispatcher ?? CreateDefaultDispatcher(
            runtimeHandler ?? new NoopContainerApplicationRuntimeHandler());
    private readonly IContainerApplicationOrchestratorRuntimeHandler? _orchestratorRuntimeHandler =
        orchestratorRuntimeHandler;
    private readonly IReadOnlyList<IDeferredContainerApplicationRuntimeSelector> _deferredRuntimeSelectors =
        deferredRuntimeSelectors?.ToArray() ?? [];

    public bool CanExecuteOrchestratorService(
        ResourceManagerResource resource,
        ResourceAction action) =>
        string.Equals(
            resource.TypeId,
            ContainerApplicationResourceTypeProvider.ResourceTypeId.ToString(),
            StringComparison.OrdinalIgnoreCase) &&
        action.Kind is ResourceActionKind.Start or ResourceActionKind.Stop &&
        !_deferredRuntimeSelectors.Any(selector => selector.IsDeferredRuntimeResourceId(resource.Id));

    public async ValueTask PrepareOrchestratorServiceAsync(
        ResourceModelGraphOrchestratorServiceProcedureContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default)
    {
        if (_orchestratorRuntimeHandler is not null)
        {
            var orchestratorDiagnostics = action.Kind switch
            {
                ResourceActionKind.Start => await _orchestratorRuntimeHandler.PrepareOrchestratorServiceAsync(
                    context.GraphResource,
                    context.Service,
                    context.ReplicaGroup,
                    context.ServiceRoutingBindings,
                    cancellationToken),
                ResourceActionKind.Stop => await _orchestratorRuntimeHandler.TearDownOrchestratorServiceRoutingAsync(
                    context.GraphResource,
                    context.Service,
                    context.ReplicaGroup,
                    context.ServiceRoutingBindings,
                    cancellationToken),
                _ => []
            };
            ThrowIfErrors(orchestratorDiagnostics);
            return;
        }

        if (action.Kind != ResourceActionKind.Start)
        {
            return;
        }

        var diagnostics = _runtimeHandler.GetStatus(context.GraphResource) == ContainerApplicationRuntimeStatus.Running
            ? await ExecuteRuntimeInstructionAsync(
                context.GraphResource,
                ContainerApplicationResourceTypeProvider.Operations.UpdateImage,
                ProviderExecutionInstructionTypes.ContainerApplicationImageApply,
                cancellationToken)
            : await ExecuteRuntimeInstructionAsync(
                context.GraphResource,
                ContainerApplicationResourceTypeProvider.Operations.Start,
                ProviderExecutionInstructionTypes.ContainerApplicationStart,
                cancellationToken);
        ThrowIfErrors(diagnostics);
    }

    public async ValueTask ReconcileOrchestratorServiceRoutingAsync(
        ResourceModelGraphOrchestratorServiceProcedureContext context,
        CancellationToken cancellationToken = default)
    {
        if (_orchestratorRuntimeHandler is null)
        {
            return;
        }

        var diagnostics = await _orchestratorRuntimeHandler.ReconcileOrchestratorServiceRoutingAsync(
            context.GraphResource,
            context.Service,
            context.ReplicaGroup,
            context.ServiceRoutingBindings,
            cancellationToken);
        ThrowIfErrors(diagnostics);
    }

    public async ValueTask ExecuteOrchestratorServiceInstanceAsync(
        ResourceModelGraphOrchestratorServiceInstanceContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default)
    {
        if (_orchestratorRuntimeHandler is not null)
        {
            var orchestratorDiagnostics = await _orchestratorRuntimeHandler.ExecuteOrchestratorServiceInstanceAsync(
                context.GraphResource,
                context.Service,
                context.Instance,
                action,
                context.ReplicaGroup,
                cancellationToken);
            ThrowIfErrors(orchestratorDiagnostics);
            return;
        }

        if (action.Kind != ResourceActionKind.Stop ||
            !IsFirstInstanceInGroup(context))
        {
            return;
        }

        var desiredReplicas = new ContainerApplicationResource(context.GraphResource).Replicas;
        var diagnostics = context.ReplicaGroup is not null &&
            context.ReplicaGroup.RequestedReplicas > desiredReplicas
                ? await ExecuteRuntimeInstructionAsync(
                    context.GraphResource,
                    ContainerApplicationResourceTypeProvider.Operations.UpdateReplicas,
                    ProviderExecutionInstructionTypes.ContainerApplicationReplicasApply,
                    cancellationToken)
                : await ExecuteRuntimeInstructionAsync(
                    context.GraphResource,
                    ContainerApplicationResourceTypeProvider.Operations.Stop,
                    ProviderExecutionInstructionTypes.ContainerApplicationStop,
                    cancellationToken);
        ThrowIfErrors(diagnostics);
    }

    private async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteRuntimeInstructionAsync(
        Resource resource,
        ResourceOperationId operationId,
        string instructionType,
        CancellationToken cancellationToken)
    {
        var result = await _dispatcher.ExecuteAsync(
            new ProviderExecutionRequest
            {
                AssignmentId = $"{resource.EffectiveResourceId}:{operationId}",
                InstructionType = instructionType,
                TargetResourceId = resource.EffectiveResourceId,
                DesiredGeneration = resource.Revision.Value,
                IdempotencyKey = $"{resource.EffectiveResourceId}:{operationId}:{resource.Revision.Value}",
                RequiredCapabilities = [ProviderExecutionCapabilities.Containers],
                TargetResourceSnapshot = resource,
                ResourceSnapshot = [resource],
                RequestedAt = DateTimeOffset.UtcNow
            },
            cancellationToken);

        return result.Diagnostics;
    }

    private static bool IsFirstInstanceInGroup(
        ResourceModelGraphOrchestratorServiceInstanceContext context)
    {
        var minimumOrdinal = context.ReplicaGroup?.Instances
            .Select(instance => instance.ReplicaOrdinal)
            .DefaultIfEmpty(context.Instance.ReplicaOrdinal)
            .Min() ?? context.Instance.ReplicaOrdinal;
        return context.Instance.ReplicaOrdinal == minimumOrdinal;
    }

    private static void ThrowIfErrors(
        IReadOnlyList<ResourceDefinitionDiagnostic> diagnostics)
    {
        var errors = diagnostics
            .Where(diagnostic => diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error)
            .ToArray();
        if (errors.Length == 0)
        {
            return;
        }

        throw new InvalidOperationException(string.Join(
            " ",
            errors.Select(diagnostic =>
                string.IsNullOrWhiteSpace(diagnostic.Target)
                    ? diagnostic.Message
                    : $"{diagnostic.Message} Target: {diagnostic.Target}.")));
    }

    private static IProviderExecutionDispatcher CreateDefaultDispatcher(
        IContainerApplicationRuntimeHandler runtimeHandler) =>
        new InProcessProviderExecutionDispatcher(
            [
                new ContainerApplicationStartExecutionHandler(runtimeHandler),
                new ContainerApplicationStopExecutionHandler(runtimeHandler),
                new ContainerApplicationImageApplyExecutionHandler(runtimeHandler),
                new ContainerApplicationReplicasApplyExecutionHandler(runtimeHandler)
            ]);
}
