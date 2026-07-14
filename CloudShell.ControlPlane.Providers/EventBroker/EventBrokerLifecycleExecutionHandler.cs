namespace CloudShell.ControlPlane.Providers;

public sealed class EventBrokerStartExecutionHandler(
    IEventBrokerRuntimeController? runtimeController = null)
    : EventBrokerLifecycleExecutionHandler(
        ProviderExecutionInstructionTypes.EventBrokerStart,
        runtimeController);

public sealed class EventBrokerStopExecutionHandler(
    IEventBrokerRuntimeController? runtimeController = null)
    : EventBrokerLifecycleExecutionHandler(
        ProviderExecutionInstructionTypes.EventBrokerStop,
        runtimeController);

public sealed class EventBrokerRestartExecutionHandler(
    IEventBrokerRuntimeController? runtimeController = null)
    : EventBrokerLifecycleExecutionHandler(
        ProviderExecutionInstructionTypes.EventBrokerRestart,
        runtimeController);

public abstract class EventBrokerLifecycleExecutionHandler(
    string instructionType,
    IEventBrokerRuntimeController? runtimeController = null) : IProviderExecutionHandler
{
    private readonly IEventBrokerRuntimeController _runtimeController =
        runtimeController ?? new NoopEventBrokerRuntimeController();

    public string InstructionType { get; } = instructionType;

    public IReadOnlyList<string> Capabilities =>
    [
        ProviderExecutionCapabilities.Processes
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
                $"Event Broker lifecycle execution does not support instruction '{request.InstructionType}'."));
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
        var diagnostics = await _runtimeController.ExecuteAsync(
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
            ProviderExecutionInstructionTypes.EventBrokerStart =>
                EventBrokerResourceTypeProvider.Operations.Start,
            ProviderExecutionInstructionTypes.EventBrokerStop =>
                EventBrokerResourceTypeProvider.Operations.Stop,
            ProviderExecutionInstructionTypes.EventBrokerRestart =>
                EventBrokerResourceTypeProvider.Operations.Restart,
            _ => (ResourceOperationId?)null
        };
}
