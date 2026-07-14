namespace CloudShell.ControlPlane.Providers;

public sealed class SecretsVaultStartExecutionHandler(
    ISecretsVaultRuntimeController? runtimeController = null)
    : SecretsVaultLifecycleExecutionHandler(
        ProviderExecutionInstructionTypes.SecretsVaultStart,
        runtimeController);

public sealed class SecretsVaultStopExecutionHandler(
    ISecretsVaultRuntimeController? runtimeController = null)
    : SecretsVaultLifecycleExecutionHandler(
        ProviderExecutionInstructionTypes.SecretsVaultStop,
        runtimeController);

public sealed class SecretsVaultRestartExecutionHandler(
    ISecretsVaultRuntimeController? runtimeController = null)
    : SecretsVaultLifecycleExecutionHandler(
        ProviderExecutionInstructionTypes.SecretsVaultRestart,
        runtimeController);

public abstract class SecretsVaultLifecycleExecutionHandler(
    string instructionType,
    ISecretsVaultRuntimeController? runtimeController = null) : IProviderExecutionHandler
{
    private readonly ISecretsVaultRuntimeController _runtimeController =
        runtimeController ?? new NoopSecretsVaultRuntimeController();

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
                $"Secrets Vault lifecycle execution does not support instruction '{request.InstructionType}'."));
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
            ProviderExecutionInstructionTypes.SecretsVaultStart =>
                SecretsVaultResourceTypeProvider.Operations.Start,
            ProviderExecutionInstructionTypes.SecretsVaultStop =>
                SecretsVaultResourceTypeProvider.Operations.Stop,
            ProviderExecutionInstructionTypes.SecretsVaultRestart =>
                SecretsVaultResourceTypeProvider.Operations.Restart,
            _ => (ResourceOperationId?)null
        };
}
