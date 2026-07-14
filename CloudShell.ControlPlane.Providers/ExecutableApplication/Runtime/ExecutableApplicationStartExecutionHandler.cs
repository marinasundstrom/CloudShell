namespace CloudShell.ControlPlane.Providers;

public sealed class ExecutableApplicationStartExecutionHandler(
    IExecutableApplicationRuntimeController? runtimeController = null) : IProviderExecutionHandler
{
    private readonly IExecutableApplicationRuntimeController _runtimeController =
        runtimeController ?? new NoopExecutableApplicationRuntimeController();

    public string InstructionType =>
        ProviderExecutionInstructionTypes.ExecutableApplicationStart;

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

        var diagnostics = await _runtimeController.StartAsync(
            context.Resource,
            cancellationToken);

        return diagnostics.Any(diagnostic =>
            diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error)
            ? ProviderExecutionResult.Failed(request, diagnostics)
            : ProviderExecutionResult.Succeeded(request, diagnostics: diagnostics);
    }
}
