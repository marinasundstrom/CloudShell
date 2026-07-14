using CloudShell.Abstractions.ResourceManager;
using System.Text.Json;

namespace CloudShell.ControlPlane.Providers;

public sealed class ContainerApplicationOrchestratorServicePrepareExecutionHandler(
    IContainerApplicationOrchestratorRuntimeHandler? runtimeHandler = null)
    : ContainerApplicationOrchestratorServiceExecutionHandler(runtimeHandler)
{
    public override string InstructionType =>
        ProviderExecutionInstructionTypes.ContainerApplicationOrchestratorServicePrepare;

    protected override ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteCoreAsync(
        IContainerApplicationOrchestratorRuntimeHandler runtimeHandler,
        Resource resource,
        ContainerApplicationOrchestratorServiceExecutionPayload payload,
        CancellationToken cancellationToken) =>
        runtimeHandler.PrepareOrchestratorServiceAsync(
            resource,
            payload.Service,
            payload.ReplicaGroup,
            payload.RoutingBindings,
            cancellationToken);
}

public sealed class ContainerApplicationRoutingReconcileExecutionHandler(
    IContainerApplicationOrchestratorRuntimeHandler? runtimeHandler = null)
    : ContainerApplicationOrchestratorServiceExecutionHandler(runtimeHandler)
{
    public override string InstructionType =>
        ProviderExecutionInstructionTypes.ContainerApplicationRoutingReconcile;

    protected override ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteCoreAsync(
        IContainerApplicationOrchestratorRuntimeHandler runtimeHandler,
        Resource resource,
        ContainerApplicationOrchestratorServiceExecutionPayload payload,
        CancellationToken cancellationToken) =>
        runtimeHandler.ReconcileOrchestratorServiceRoutingAsync(
            resource,
            payload.Service,
            payload.ReplicaGroup,
            payload.RoutingBindings,
            cancellationToken);
}

public sealed class ContainerApplicationRoutingTearDownExecutionHandler(
    IContainerApplicationOrchestratorRuntimeHandler? runtimeHandler = null)
    : ContainerApplicationOrchestratorServiceExecutionHandler(runtimeHandler)
{
    public override string InstructionType =>
        ProviderExecutionInstructionTypes.ContainerApplicationRoutingTearDown;

    protected override ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteCoreAsync(
        IContainerApplicationOrchestratorRuntimeHandler runtimeHandler,
        Resource resource,
        ContainerApplicationOrchestratorServiceExecutionPayload payload,
        CancellationToken cancellationToken) =>
        runtimeHandler.TearDownOrchestratorServiceRoutingAsync(
            resource,
            payload.Service,
            payload.ReplicaGroup,
            payload.RoutingBindings,
            cancellationToken);
}

public abstract class ContainerApplicationOrchestratorServiceExecutionHandler(
    IContainerApplicationOrchestratorRuntimeHandler? runtimeHandler) : IProviderExecutionHandler
{
    private readonly IContainerApplicationOrchestratorRuntimeHandler? _runtimeHandler = runtimeHandler;

    public abstract string InstructionType { get; }

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

        var payload = request.Payload?.Deserialize<ContainerApplicationOrchestratorServiceExecutionPayload>();
        if (payload is null)
        {
            return ProviderExecutionResult.Unavailable(
                request,
                ProviderExecutionDiagnosticCodes.PayloadMissing,
                $"Provider execution instruction '{request.InstructionType}' requires orchestrator service payload.");
        }

        var diagnostics = await ExecuteCoreAsync(
            _runtimeHandler,
            context.Resource,
            payload,
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

    protected abstract ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteCoreAsync(
        IContainerApplicationOrchestratorRuntimeHandler runtimeHandler,
        Resource resource,
        ContainerApplicationOrchestratorServiceExecutionPayload payload,
        CancellationToken cancellationToken);
}

public sealed record ContainerApplicationOrchestratorServiceExecutionPayload(
    ResourceOrchestratorService Service,
    ResourceOrchestratorReplicaGroup? ReplicaGroup,
    IReadOnlyList<ResourceOrchestratorServiceRoutingBindingDefinition> RoutingBindings);
