using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceModel;
using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;

namespace CloudShell.ControlPlane.Providers;

public sealed class ContainerApplicationResourceModelGraphServiceExecutor(
    IContainerApplicationRuntimeHandler? runtimeHandler = null,
    IContainerApplicationOrchestratorRuntimeHandler? orchestratorRuntimeHandler = null) : IResourceModelGraphOrchestratorServiceExecutor
{
    private readonly IContainerApplicationRuntimeHandler _runtimeHandler =
        runtimeHandler ?? new NoopContainerApplicationRuntimeHandler();
    private readonly IContainerApplicationOrchestratorRuntimeHandler? _orchestratorRuntimeHandler =
        orchestratorRuntimeHandler;

    public bool CanExecuteOrchestratorService(
        ResourceManagerResource resource,
        ResourceAction action) =>
        string.Equals(
            resource.TypeId,
            ContainerApplicationResourceTypeProvider.ResourceTypeId.ToString(),
            StringComparison.OrdinalIgnoreCase) &&
        action.Kind is ResourceActionKind.Start or ResourceActionKind.Stop;

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
                    cancellationToken),
                ResourceActionKind.Stop => await _orchestratorRuntimeHandler.TearDownOrchestratorServiceRoutingAsync(
                    context.GraphResource,
                    context.Service,
                    context.ReplicaGroup,
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
            ? await _runtimeHandler.ApplyImageAsync(
                context.GraphResource,
                cancellationToken)
            : await _runtimeHandler.ExecuteLifecycleAsync(
                context.GraphResource,
                ResourceActionIds.Start,
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
                ? await _runtimeHandler.ApplyReplicasAsync(
                    context.GraphResource,
                    cancellationToken)
                : await _runtimeHandler.ExecuteLifecycleAsync(
                    context.GraphResource,
                    ResourceActionIds.Stop,
                    cancellationToken);
        ThrowIfErrors(diagnostics);
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
}
