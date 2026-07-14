namespace CloudShell.ControlPlane.Providers;

public sealed class SqlServerStartExecutionHandler(
    ISqlServerRuntimeHandler? runtimeHandler = null)
    : SqlServerLifecycleExecutionHandler(
        ProviderExecutionInstructionTypes.SqlServerStart,
        runtimeHandler);

public sealed class SqlServerStopExecutionHandler(
    ISqlServerRuntimeHandler? runtimeHandler = null)
    : SqlServerLifecycleExecutionHandler(
        ProviderExecutionInstructionTypes.SqlServerStop,
        runtimeHandler);

public sealed class SqlServerRestartExecutionHandler(
    ISqlServerRuntimeHandler? runtimeHandler = null)
    : SqlServerLifecycleExecutionHandler(
        ProviderExecutionInstructionTypes.SqlServerRestart,
        runtimeHandler);

public abstract class SqlServerLifecycleExecutionHandler(
    string instructionType,
    ISqlServerRuntimeHandler? runtimeHandler = null) : IProviderExecutionHandler
{
    private readonly ISqlServerRuntimeHandler _runtimeHandler =
        runtimeHandler ?? new NoopSqlServerRuntimeHandler();

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
                $"SQL Server lifecycle execution does not support instruction '{request.InstructionType}'."));
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
            ProviderExecutionInstructionTypes.SqlServerStart =>
                SqlServerResourceTypeProvider.Operations.Start,
            ProviderExecutionInstructionTypes.SqlServerStop =>
                SqlServerResourceTypeProvider.Operations.Stop,
            ProviderExecutionInstructionTypes.SqlServerRestart =>
                SqlServerResourceTypeProvider.Operations.Restart,
            _ => (ResourceOperationId?)null
        };
}
