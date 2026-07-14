namespace CloudShell.ControlPlane.Providers;

public sealed class ServiceReconcileExecutionHandler(
    IServiceReconciler? reconciler = null) : IProviderExecutionHandler
{
    private readonly IServiceReconciler _reconciler =
        reconciler ?? new NoopServiceReconciler();

    public string InstructionType =>
        ProviderExecutionInstructionTypes.ServiceReconcile;

    public IReadOnlyList<string> Capabilities =>
    [
        ProviderExecutionCapabilities.ServiceReconciliation
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

        var diagnostics = await _reconciler.ReconcileAsync(
            context.Resource,
            cancellationToken);

        return diagnostics.Any(diagnostic =>
            diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error)
            ? ProviderExecutionResult.Failed(request, diagnostics)
            : ProviderExecutionResult.Succeeded(request, diagnostics: diagnostics);
    }
}
