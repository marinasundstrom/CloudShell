namespace CloudShell.ControlPlane.Providers;

public sealed class GoAppStartExecutionHandler(
    IGoAppRuntimeController? runtimeController = null)
    : GoAppLifecycleExecutionHandler(
        GoAppResourceTypeProvider.Operations.Start,
        ProviderExecutionInstructionTypes.GoAppStart,
        runtimeController);

public sealed class GoAppStopExecutionHandler(
    IGoAppRuntimeController? runtimeController = null)
    : GoAppLifecycleExecutionHandler(
        GoAppResourceTypeProvider.Operations.Stop,
        ProviderExecutionInstructionTypes.GoAppStop,
        runtimeController);

public sealed class GoAppRestartExecutionHandler(
    IGoAppRuntimeController? runtimeController = null)
    : GoAppLifecycleExecutionHandler(
        GoAppResourceTypeProvider.Operations.Restart,
        ProviderExecutionInstructionTypes.GoAppRestart,
        runtimeController);

public abstract class GoAppLifecycleExecutionHandler(
    ResourceOperationId operationId,
    string instructionType,
    IGoAppRuntimeController? runtimeController = null) : IProviderExecutionHandler
{
    private readonly ResourceOperationId _operationId = operationId;
    private readonly IGoAppRuntimeController _runtimeController =
        runtimeController ?? new NoopGoAppRuntimeController();

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
