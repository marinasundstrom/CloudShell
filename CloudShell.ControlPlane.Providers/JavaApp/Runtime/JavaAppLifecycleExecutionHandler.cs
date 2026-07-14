namespace CloudShell.ControlPlane.Providers;

public sealed class JavaAppStartExecutionHandler(
    IJavaAppRuntimeController? runtimeController = null)
    : JavaAppLifecycleExecutionHandler(
        JavaAppResourceTypeProvider.Operations.Start,
        ProviderExecutionInstructionTypes.JavaAppStart,
        runtimeController);

public sealed class JavaAppStopExecutionHandler(
    IJavaAppRuntimeController? runtimeController = null)
    : JavaAppLifecycleExecutionHandler(
        JavaAppResourceTypeProvider.Operations.Stop,
        ProviderExecutionInstructionTypes.JavaAppStop,
        runtimeController);

public sealed class JavaAppRestartExecutionHandler(
    IJavaAppRuntimeController? runtimeController = null)
    : JavaAppLifecycleExecutionHandler(
        JavaAppResourceTypeProvider.Operations.Restart,
        ProviderExecutionInstructionTypes.JavaAppRestart,
        runtimeController);

public abstract class JavaAppLifecycleExecutionHandler(
    ResourceOperationId operationId,
    string instructionType,
    IJavaAppRuntimeController? runtimeController = null) : IProviderExecutionHandler
{
    private readonly ResourceOperationId _operationId = operationId;
    private readonly IJavaAppRuntimeController _runtimeController =
        runtimeController ?? new NoopJavaAppRuntimeController();

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
