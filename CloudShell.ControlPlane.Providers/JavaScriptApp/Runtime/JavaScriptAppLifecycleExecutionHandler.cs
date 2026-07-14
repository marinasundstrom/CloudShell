namespace CloudShell.ControlPlane.Providers;

public sealed class JavaScriptAppStartExecutionHandler(
    IJavaScriptAppRuntimeController? runtimeController = null)
    : JavaScriptAppLifecycleExecutionHandler(
        JavaScriptAppResourceTypeProvider.Operations.Start,
        ProviderExecutionInstructionTypes.JavaScriptAppStart,
        runtimeController);

public sealed class JavaScriptAppStopExecutionHandler(
    IJavaScriptAppRuntimeController? runtimeController = null)
    : JavaScriptAppLifecycleExecutionHandler(
        JavaScriptAppResourceTypeProvider.Operations.Stop,
        ProviderExecutionInstructionTypes.JavaScriptAppStop,
        runtimeController);

public sealed class JavaScriptAppRestartExecutionHandler(
    IJavaScriptAppRuntimeController? runtimeController = null)
    : JavaScriptAppLifecycleExecutionHandler(
        JavaScriptAppResourceTypeProvider.Operations.Restart,
        ProviderExecutionInstructionTypes.JavaScriptAppRestart,
        runtimeController);

public abstract class JavaScriptAppLifecycleExecutionHandler(
    ResourceOperationId operationId,
    string instructionType,
    IJavaScriptAppRuntimeController? runtimeController = null) : IProviderExecutionHandler
{
    private readonly ResourceOperationId _operationId = operationId;
    private readonly IJavaScriptAppRuntimeController _runtimeController =
        runtimeController ?? new NoopJavaScriptAppRuntimeController();

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
