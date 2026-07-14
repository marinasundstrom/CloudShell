namespace CloudShell.ControlPlane.Providers;

public sealed class MacOSHostNetworkEndpointMappingExecutionHandler(
    IMacOSHostNetworkEndpointMappingReconciler? reconciler = null) :
    IProviderExecutionHandler
{
    private readonly IMacOSHostNetworkEndpointMappingReconciler _reconciler =
        reconciler ?? new NoopMacOSHostNetworkEndpointMappingReconciler();

    public string InstructionType =>
        ProviderExecutionInstructionTypes.MacOSHostNetworkEndpointReconcile;

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
