namespace CloudShell.ControlPlane.Providers;

public sealed class ContainerApplicationStartExecutionHandler(
    IContainerApplicationRuntimeHandler? runtimeHandler = null)
    : ContainerApplicationLifecycleExecutionHandler(
        ProviderExecutionInstructionTypes.ContainerApplicationStart,
        runtimeHandler);

public sealed class ContainerApplicationStopExecutionHandler(
    IContainerApplicationRuntimeHandler? runtimeHandler = null)
    : ContainerApplicationLifecycleExecutionHandler(
        ProviderExecutionInstructionTypes.ContainerApplicationStop,
        runtimeHandler);

public sealed class ContainerApplicationRestartExecutionHandler(
    IContainerApplicationRuntimeHandler? runtimeHandler = null)
    : ContainerApplicationLifecycleExecutionHandler(
        ProviderExecutionInstructionTypes.ContainerApplicationRestart,
        runtimeHandler);

public abstract class ContainerApplicationLifecycleExecutionHandler(
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
        var operationId = GetOperationId(request.InstructionType);
        if (operationId is not { } resolvedOperationId)
        {
            return ValueTask.FromResult(ProviderExecutionResult.Unavailable(
                request,
                ProviderExecutionDiagnosticCodes.HandlerMissing,
                $"Container Application lifecycle execution does not support instruction '{request.InstructionType}'."));
        }

        var context = request.TryCreateProjectionExecutionContext();
        if (context is null)
        {
            return ValueTask.FromResult(ProviderExecutionResult.Unavailable(
                request,
                ProviderExecutionDiagnosticCodes.ResourceSnapshotMissing,
                $"Provider execution instruction '{request.InstructionType}' requires a resource snapshot for '{request.TargetResourceId}'."));
        }

        return ExecuteCoreAsync(request, context.Resource, resolvedOperationId, cancellationToken);
    }

    private async ValueTask<ProviderExecutionResult> ExecuteCoreAsync(
        ProviderExecutionRequest request,
        Resource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken)
    {
        var diagnostics = await _runtimeHandler.ExecuteLifecycleAsync(
            resource,
            operationId,
            cancellationToken);

        return diagnostics.Any(diagnostic =>
            diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error)
            ? ProviderExecutionResult.Failed(request, diagnostics)
            : ProviderExecutionResult.Succeeded(request, diagnostics: diagnostics);
    }

    private static ResourceOperationId? GetOperationId(string instructionType) =>
        instructionType switch
        {
            ProviderExecutionInstructionTypes.ContainerApplicationStart =>
                ContainerApplicationResourceTypeProvider.Operations.Start,
            ProviderExecutionInstructionTypes.ContainerApplicationStop =>
                ContainerApplicationResourceTypeProvider.Operations.Stop,
            ProviderExecutionInstructionTypes.ContainerApplicationRestart =>
                ContainerApplicationResourceTypeProvider.Operations.Restart,
            _ => (ResourceOperationId?)null
        };
}
