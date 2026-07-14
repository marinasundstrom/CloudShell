namespace CloudShell.ControlPlane.Providers;

public sealed class DockerContainerStartExecutionHandler(
    IDockerContainerRuntimeHandler? runtimeHandler = null)
    : DockerContainerLifecycleExecutionHandler(
        ProviderExecutionInstructionTypes.ContainerStart,
        runtimeHandler);

public sealed class DockerContainerStopExecutionHandler(
    IDockerContainerRuntimeHandler? runtimeHandler = null)
    : DockerContainerLifecycleExecutionHandler(
        ProviderExecutionInstructionTypes.ContainerStop,
        runtimeHandler);

public sealed class DockerContainerPauseExecutionHandler(
    IDockerContainerRuntimeHandler? runtimeHandler = null)
    : DockerContainerLifecycleExecutionHandler(
        ProviderExecutionInstructionTypes.ContainerPause,
        runtimeHandler);

public sealed class DockerContainerRestartExecutionHandler(
    IDockerContainerRuntimeHandler? runtimeHandler = null)
    : DockerContainerLifecycleExecutionHandler(
        ProviderExecutionInstructionTypes.ContainerRestart,
        runtimeHandler);

public sealed class DockerContainerUnpauseExecutionHandler(
    IDockerContainerRuntimeHandler? runtimeHandler = null)
    : DockerContainerLifecycleExecutionHandler(
        ProviderExecutionInstructionTypes.ContainerUnpause,
        runtimeHandler);

public abstract class DockerContainerLifecycleExecutionHandler(
    string instructionType,
    IDockerContainerRuntimeHandler? runtimeHandler = null) : IProviderExecutionHandler
{
    private readonly IDockerContainerRuntimeHandler _runtimeHandler =
        runtimeHandler ?? new NoopDockerContainerRuntimeHandler();

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
                $"Docker container lifecycle execution does not support instruction '{request.InstructionType}'."));
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
            ProviderExecutionInstructionTypes.ContainerStart =>
                DockerContainerResourceTypeProvider.Operations.Start,
            ProviderExecutionInstructionTypes.ContainerStop =>
                DockerContainerResourceTypeProvider.Operations.Stop,
            ProviderExecutionInstructionTypes.ContainerPause =>
                DockerContainerResourceTypeProvider.Operations.Pause,
            ProviderExecutionInstructionTypes.ContainerRestart =>
                DockerContainerResourceTypeProvider.Operations.Restart,
            ProviderExecutionInstructionTypes.ContainerUnpause =>
                DockerContainerResourceTypeProvider.Operations.Unpause,
            _ => (ResourceOperationId?)null
        };
}
