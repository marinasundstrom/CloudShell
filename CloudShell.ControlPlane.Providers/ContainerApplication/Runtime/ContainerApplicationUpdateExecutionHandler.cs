namespace CloudShell.ControlPlane.Providers;

public sealed class ContainerApplicationImageApplyExecutionHandler(
    IContainerApplicationRuntimeHandler? runtimeHandler = null)
    : ContainerApplicationUpdateExecutionHandler(
        ProviderExecutionInstructionTypes.ContainerApplicationImageApply,
        runtimeHandler);

public sealed class ContainerApplicationReplicasApplyExecutionHandler(
    IContainerApplicationRuntimeHandler? runtimeHandler = null)
    : ContainerApplicationUpdateExecutionHandler(
        ProviderExecutionInstructionTypes.ContainerApplicationReplicasApply,
        runtimeHandler);

public abstract class ContainerApplicationUpdateExecutionHandler(
    string instructionType,
    IContainerApplicationRuntimeHandler? runtimeHandler = null) : IProviderExecutionHandler
{
    private readonly IContainerApplicationRuntimeHandler _runtimeHandler =
        runtimeHandler ?? new NoopContainerApplicationRuntimeHandler();

    public string InstructionType { get; } = instructionType;

    public IReadOnlyList<string> Capabilities =>
    [
        ProviderExecutionCapabilities.Containers
    ];

    public ValueTask<ProviderExecutionResult> ExecuteAsync(
        ProviderExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        var context = request.TryCreateProjectionExecutionContext();
        if (context is null)
        {
            return ValueTask.FromResult(ProviderExecutionResult.Unavailable(
                request,
                ProviderExecutionDiagnosticCodes.ResourceSnapshotMissing,
                $"Provider execution instruction '{request.InstructionType}' requires a resource snapshot for '{request.TargetResourceId}'."));
        }

        return ExecuteCoreAsync(request, context.Resource, cancellationToken);
    }

    private async ValueTask<ProviderExecutionResult> ExecuteCoreAsync(
        ProviderExecutionRequest request,
        Resource resource,
        CancellationToken cancellationToken)
    {
        var diagnostics = request.InstructionType switch
        {
            ProviderExecutionInstructionTypes.ContainerApplicationImageApply =>
                await _runtimeHandler.ApplyImageAsync(resource, cancellationToken),
            ProviderExecutionInstructionTypes.ContainerApplicationReplicasApply =>
                await _runtimeHandler.ApplyReplicasAsync(resource, cancellationToken),
            _ => null
        };

        if (diagnostics is null)
        {
            return ProviderExecutionResult.Unavailable(
                request,
                ProviderExecutionDiagnosticCodes.HandlerMissing,
                $"Container Application update execution does not support instruction '{request.InstructionType}'.");
        }

        return diagnostics.Any(diagnostic =>
            diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error)
            ? ProviderExecutionResult.Failed(request, diagnostics)
            : ProviderExecutionResult.Succeeded(request, diagnostics: diagnostics);
    }
}
