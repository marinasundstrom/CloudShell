namespace CloudShell.ControlPlane.Providers;

public sealed class PythonAppStartExecutionHandler(
    IPythonAppRuntimeController? runtimeController = null)
    : PythonAppLifecycleExecutionHandler(
        PythonAppResourceTypeProvider.Operations.Start,
        ProviderExecutionInstructionTypes.PythonAppStart,
        runtimeController);

public sealed class PythonAppStopExecutionHandler(
    IPythonAppRuntimeController? runtimeController = null)
    : PythonAppLifecycleExecutionHandler(
        PythonAppResourceTypeProvider.Operations.Stop,
        ProviderExecutionInstructionTypes.PythonAppStop,
        runtimeController);

public sealed class PythonAppRestartExecutionHandler(
    IPythonAppRuntimeController? runtimeController = null)
    : PythonAppLifecycleExecutionHandler(
        PythonAppResourceTypeProvider.Operations.Restart,
        ProviderExecutionInstructionTypes.PythonAppRestart,
        runtimeController);

public abstract class PythonAppLifecycleExecutionHandler(
    ResourceOperationId operationId,
    string instructionType,
    IPythonAppRuntimeController? runtimeController = null) : IProviderExecutionHandler
{
    private readonly ResourceOperationId _operationId = operationId;
    private readonly IPythonAppRuntimeController _runtimeController =
        runtimeController ?? new NoopPythonAppRuntimeController();

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
