namespace CloudShell.ControlPlane.Providers;

public sealed class RabbitMQStartExecutionHandler(
    IRabbitMQRuntimeHandler? runtimeHandler = null)
    : RabbitMQLifecycleExecutionHandler(
        ProviderExecutionInstructionTypes.RabbitMQStart,
        runtimeHandler);

public sealed class RabbitMQStopExecutionHandler(
    IRabbitMQRuntimeHandler? runtimeHandler = null)
    : RabbitMQLifecycleExecutionHandler(
        ProviderExecutionInstructionTypes.RabbitMQStop,
        runtimeHandler);

public sealed class RabbitMQRestartExecutionHandler(
    IRabbitMQRuntimeHandler? runtimeHandler = null)
    : RabbitMQLifecycleExecutionHandler(
        ProviderExecutionInstructionTypes.RabbitMQRestart,
        runtimeHandler);

public abstract class RabbitMQLifecycleExecutionHandler(
    string instructionType,
    IRabbitMQRuntimeHandler? runtimeHandler = null) : IProviderExecutionHandler
{
    private readonly IRabbitMQRuntimeHandler _runtimeHandler =
        runtimeHandler ?? new NoopRabbitMQRuntimeHandler();

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
                $"RabbitMQ lifecycle execution does not support instruction '{request.InstructionType}'."));
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
            ProviderExecutionInstructionTypes.RabbitMQStart =>
                RabbitMQResourceTypeProvider.Operations.Start,
            ProviderExecutionInstructionTypes.RabbitMQStop =>
                RabbitMQResourceTypeProvider.Operations.Stop,
            ProviderExecutionInstructionTypes.RabbitMQRestart =>
                RabbitMQResourceTypeProvider.Operations.Restart,
            _ => (ResourceOperationId?)null
        };
}
