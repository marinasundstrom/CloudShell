using CloudShell.Abstractions.ResourceManager;
using System.Text.Json;

namespace CloudShell.ControlPlane.Providers;

public sealed class ContainerApplicationServiceInstanceStartExecutionHandler(
    IContainerApplicationOrchestratorRuntimeHandler? runtimeHandler = null)
    : ContainerApplicationServiceInstanceExecutionHandler(
        ProviderExecutionInstructionTypes.ContainerApplicationServiceInstanceStart,
        ResourceActionKind.Start,
        runtimeHandler);

public sealed class ContainerApplicationServiceInstanceStopExecutionHandler(
    IContainerApplicationOrchestratorRuntimeHandler? runtimeHandler = null)
    : ContainerApplicationServiceInstanceExecutionHandler(
        ProviderExecutionInstructionTypes.ContainerApplicationServiceInstanceStop,
        ResourceActionKind.Stop,
        runtimeHandler);

public abstract class ContainerApplicationServiceInstanceExecutionHandler(
    string instructionType,
    ResourceActionKind actionKind,
    IContainerApplicationOrchestratorRuntimeHandler? runtimeHandler) : IProviderExecutionHandler
{
    private readonly ResourceActionKind _actionKind = actionKind;
    private readonly IContainerApplicationOrchestratorRuntimeHandler? _runtimeHandler = runtimeHandler;

    public string InstructionType { get; } = instructionType;

    public IReadOnlyList<string> Capabilities =>
    [
        ProviderExecutionCapabilities.Containers
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

        var payload = request.Payload?.Deserialize<ContainerApplicationServiceInstanceExecutionPayload>();
        if (payload is null)
        {
            return ProviderExecutionResult.Unavailable(
                request,
                ProviderExecutionDiagnosticCodes.PayloadMissing,
                $"Provider execution instruction '{request.InstructionType}' requires service instance payload.");
        }

        if (payload.Action.Kind != _actionKind)
        {
            return ProviderExecutionResult.Unavailable(
                request,
                ProviderExecutionDiagnosticCodes.PayloadInvalid,
                $"Provider execution instruction '{request.InstructionType}' requires action kind '{_actionKind}' but received '{payload.Action.Kind}'.");
        }

        var diagnostics = await _runtimeHandler.ExecuteOrchestratorServiceInstanceAsync(
            context.Resource,
            payload.Service,
            payload.Instance,
            payload.Action,
            payload.ReplicaGroup,
            cancellationToken);

        return diagnostics.Any(diagnostic =>
            diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error)
            ? ProviderExecutionResult.Failed(request, diagnostics)
            : ProviderExecutionResult.Succeeded(
                request,
                diagnostics: diagnostics,
                observations: new Dictionary<string, string>
                {
                    ["actionKind"] = payload.Action.Kind.ToString(),
                    ["replicaOrdinal"] = payload.Instance.ReplicaOrdinal.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    ["replicaCount"] = payload.Instance.ReplicaCount.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    ["hasReplicaGroup"] = payload.ReplicaGroup is null ? "false" : "true"
                });
    }
}

public sealed record ContainerApplicationServiceInstanceExecutionPayload(
    ResourceOrchestratorService Service,
    ResourceOrchestratorServiceInstance Instance,
    ResourceAction Action,
    ResourceOrchestratorReplicaGroup? ReplicaGroup);
