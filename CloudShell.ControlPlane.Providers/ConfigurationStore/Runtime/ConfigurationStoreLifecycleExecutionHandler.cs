namespace CloudShell.ControlPlane.Providers;

public sealed class ConfigurationStoreStartExecutionHandler(
    IConfigurationStoreRuntimeController? runtimeController = null)
    : ConfigurationStoreLifecycleExecutionHandler(
        ProviderExecutionInstructionTypes.ConfigurationStoreStart,
        runtimeController);

public sealed class ConfigurationStoreStopExecutionHandler(
    IConfigurationStoreRuntimeController? runtimeController = null)
    : ConfigurationStoreLifecycleExecutionHandler(
        ProviderExecutionInstructionTypes.ConfigurationStoreStop,
        runtimeController);

public sealed class ConfigurationStoreRestartExecutionHandler(
    IConfigurationStoreRuntimeController? runtimeController = null)
    : ConfigurationStoreLifecycleExecutionHandler(
        ProviderExecutionInstructionTypes.ConfigurationStoreRestart,
        runtimeController);

public abstract class ConfigurationStoreLifecycleExecutionHandler(
    string instructionType,
    IConfigurationStoreRuntimeController? runtimeController = null) : IProviderExecutionHandler
{
    private readonly IConfigurationStoreRuntimeController _runtimeController =
        runtimeController ?? new NoopConfigurationStoreRuntimeController();

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
                $"Configuration Store lifecycle execution does not support instruction '{request.InstructionType}'."));
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
            ProviderExecutionInstructionTypes.ConfigurationStoreStart =>
                ConfigurationStoreResourceTypeProvider.Operations.Start,
            ProviderExecutionInstructionTypes.ConfigurationStoreStop =>
                ConfigurationStoreResourceTypeProvider.Operations.Stop,
            ProviderExecutionInstructionTypes.ConfigurationStoreRestart =>
                ConfigurationStoreResourceTypeProvider.Operations.Restart,
            _ => (ResourceOperationId?)null
        };
}
