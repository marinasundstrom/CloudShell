namespace CloudShell.ControlPlane.Providers;

public sealed class DnsZoneNameMappingExecutionHandler(
    IDnsZoneNameMappingReconciler? reconciler = null) : IProviderExecutionHandler
{
    private readonly IDnsZoneNameMappingReconciler _reconciler =
        reconciler ?? new NoopDnsZoneNameMappingReconciler();

    public string InstructionType =>
        ProviderExecutionInstructionTypes.DnsNameMappingReconcile;

    public IReadOnlyList<string> Capabilities { get; } =
    [
        ProviderExecutionCapabilities.DnsNameMappings
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

        var diagnostics = await _reconciler.ReconcileNameMappingsAsync(
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
