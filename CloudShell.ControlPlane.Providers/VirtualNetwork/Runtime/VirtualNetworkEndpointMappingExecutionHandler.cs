namespace CloudShell.ControlPlane.Providers;

public sealed class VirtualNetworkEndpointMappingExecutionHandler(
    IVirtualNetworkEndpointMappingReconciler? reconciler = null) : IProviderExecutionHandler
{
    private readonly IVirtualNetworkEndpointMappingReconciler _reconciler =
        reconciler ?? new NoopVirtualNetworkEndpointMappingReconciler();

    public string InstructionType =>
        ProviderExecutionInstructionTypes.VirtualNetworkEndpointReconcile;

    public IReadOnlyList<string> Capabilities { get; } =
    [
        ProviderExecutionCapabilities.VirtualNetworking
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
            : ProviderExecutionResult.Succeeded(
                request,
                diagnostics: diagnostics,
                observations: new Dictionary<string, string>
                {
                    ["diagnosticCount"] = diagnostics.Count.ToString(
                        System.Globalization.CultureInfo.InvariantCulture)
                });
    }
}
