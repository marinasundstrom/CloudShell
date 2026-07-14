namespace CloudShell.ControlPlane.Providers;

public sealed class AspNetCoreProjectStartExecutionHandler(
    IAspNetCoreProjectRuntimeController? runtimeController = null)
    : AspNetCoreProjectLifecycleExecutionHandler(
        AspNetCoreProjectResourceTypeProvider.Operations.Start,
        ProviderExecutionInstructionTypes.AspNetCoreProjectStart,
        runtimeController);

public sealed class AspNetCoreProjectStopExecutionHandler(
    IAspNetCoreProjectRuntimeController? runtimeController = null)
    : AspNetCoreProjectLifecycleExecutionHandler(
        AspNetCoreProjectResourceTypeProvider.Operations.Stop,
        ProviderExecutionInstructionTypes.AspNetCoreProjectStop,
        runtimeController);

public sealed class AspNetCoreProjectRestartExecutionHandler(
    IAspNetCoreProjectRuntimeController? runtimeController = null)
    : AspNetCoreProjectLifecycleExecutionHandler(
        AspNetCoreProjectResourceTypeProvider.Operations.Restart,
        ProviderExecutionInstructionTypes.AspNetCoreProjectRestart,
        runtimeController);

public abstract class AspNetCoreProjectLifecycleExecutionHandler(
    ResourceOperationId operationId,
    string instructionType,
    IAspNetCoreProjectRuntimeController? runtimeController = null) : IProviderExecutionHandler
{
    private readonly ResourceOperationId _operationId = operationId;
    private readonly IAspNetCoreProjectRuntimeController _runtimeController =
        runtimeController ?? new NoopAspNetCoreProjectRuntimeController();

    public string InstructionType { get; } = instructionType;

    public IReadOnlyList<string> Capabilities =>
    [
        ProviderExecutionCapabilities.Processes
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

        var diagnostics = await _runtimeController.ExecuteAsync(
            context.Resource,
            _operationId,
            cancellationToken);

        return diagnostics.Any(diagnostic =>
            diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error)
            ? ProviderExecutionResult.Failed(request, diagnostics)
            : ProviderExecutionResult.Succeeded(request, diagnostics: diagnostics);
    }
}
