using CloudShell.Abstractions.ResourceManager;
using System.Text.Json;

namespace CloudShell.ControlPlane.Providers;

public sealed class ContainerApplicationRoutingReconcileExecutionHandler(
    IContainerApplicationOrchestratorRuntimeHandler? runtimeHandler = null) : IProviderExecutionHandler
{
    private readonly IContainerApplicationOrchestratorRuntimeHandler? _runtimeHandler = runtimeHandler;

    public string InstructionType =>
        ProviderExecutionInstructionTypes.ContainerApplicationRoutingReconcile;

    public IReadOnlyList<string> Capabilities =>
    [
        ProviderExecutionCapabilities.Containers,
        ProviderExecutionCapabilities.LoadBalancing
    ];

    public async ValueTask<ProviderExecutionResult> ExecuteAsync(
        ProviderExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        var context = request.TryCreateProjectionExecutionContext();
        if (context is null)
        {
            return ProviderExecutionResult.Unavailable(
                request,
                ProviderExecutionDiagnosticCodes.ResourceSnapshotMissing,
                $"Provider execution instruction '{request.InstructionType}' requires a resource snapshot for '{request.TargetResourceId}'.");
        }

        if (_runtimeHandler is null)
        {
            return ProviderExecutionResult.Succeeded(request);
        }

        var payload = request.Payload?.Deserialize<ContainerApplicationRoutingReconcileExecutionPayload>();
        if (payload is null)
        {
            return ProviderExecutionResult.Unavailable(
                request,
                ProviderExecutionDiagnosticCodes.PayloadMissing,
                $"Provider execution instruction '{request.InstructionType}' requires routing reconciliation payload.");
        }

        var diagnostics = await _runtimeHandler.ReconcileOrchestratorServiceRoutingAsync(
            context.Resource,
            payload.Service,
            payload.ReplicaGroup,
            payload.RoutingBindings,
            cancellationToken);

        return diagnostics.Any(diagnostic =>
            diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error)
            ? ProviderExecutionResult.Failed(request, diagnostics)
            : ProviderExecutionResult.Succeeded(
                request,
                diagnostics: diagnostics,
                observations: new Dictionary<string, string>
                {
                    ["routingBindingCount"] = payload.RoutingBindings.Count.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    ["hasReplicaGroup"] = payload.ReplicaGroup is null ? "false" : "true"
                });
    }
}

public sealed record ContainerApplicationRoutingReconcileExecutionPayload(
    ResourceOrchestratorService Service,
    ResourceOrchestratorReplicaGroup? ReplicaGroup,
    IReadOnlyList<ResourceOrchestratorServiceRoutingBindingDefinition> RoutingBindings);
