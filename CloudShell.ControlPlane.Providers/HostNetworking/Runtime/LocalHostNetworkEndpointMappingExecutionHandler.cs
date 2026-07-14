namespace CloudShell.ControlPlane.Providers;

public sealed class LocalHostNetworkEndpointMappingExecutionHandler(
    ILocalHostNetworkEndpointMappingReconciler? reconciler = null) :
    IProviderExecutionHandler
{
    private readonly ILocalHostNetworkEndpointMappingReconciler _reconciler =
        reconciler ?? new NoopLocalHostNetworkEndpointMappingReconciler();

    public string InstructionType =>
        ProviderExecutionInstructionTypes.LocalHostNetworkEndpointReconcile;

    public IReadOnlyList<string> Capabilities { get; } =
    [
        ProviderExecutionCapabilities.HostNetworking
    ];

    public async ValueTask<ProviderExecutionResult> ExecuteAsync(
        ProviderExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var context = request.TryCreateProjectionExecutionContext();
        if (context is null)
        {
            return ProviderExecutionResult.Unavailable(
                request,
                ProviderExecutionDiagnosticCodes.ResourceSnapshotMissing,
                $"Provider execution instruction '{request.InstructionType}' requires a resource snapshot for '{request.TargetResourceId}'.");
        }

        var diagnostics = await _reconciler.ReconcileEndpointMappingsAsync(
            context.Resource,
            context,
            cancellationToken);

        return diagnostics.Any(diagnostic =>
            diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error)
            ? ProviderExecutionResult.Failed(request, diagnostics)
            : ProviderExecutionResult.Succeeded(request, diagnostics: diagnostics);
    }
}
