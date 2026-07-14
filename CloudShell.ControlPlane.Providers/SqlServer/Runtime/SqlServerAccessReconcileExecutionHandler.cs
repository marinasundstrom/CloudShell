namespace CloudShell.ControlPlane.Providers;

public sealed class SqlServerAccessReconcileExecutionHandler(
    ISqlServerAccessReconciler? accessReconciler = null) : IProviderExecutionHandler
{
    private readonly ISqlServerAccessReconciler _accessReconciler =
        accessReconciler ?? new NoopSqlServerAccessReconciler();

    public string InstructionType =>
        ProviderExecutionInstructionTypes.SqlServerAccessReconcile;

    public IReadOnlyList<string> Capabilities { get; } =
    [
        ProviderExecutionCapabilities.SqlServerAccess
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

        var diagnostics = await _accessReconciler.ReconcileAccessAsync(
            context.Resource,
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
