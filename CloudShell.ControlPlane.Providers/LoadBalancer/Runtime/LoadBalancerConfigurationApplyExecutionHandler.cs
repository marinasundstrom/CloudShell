namespace CloudShell.ControlPlane.Providers;

public sealed class LoadBalancerConfigurationApplyExecutionHandler(
    ILoadBalancerConfigurationApplier? configurationApplier = null) : IProviderExecutionHandler
{
    private readonly ILoadBalancerConfigurationApplier _configurationApplier =
        configurationApplier ?? new NoopLoadBalancerConfigurationApplier();

    public string InstructionType =>
        ProviderExecutionInstructionTypes.LoadBalancerConfigurationApply;

    public IReadOnlyList<string> Capabilities =>
    [
        ProviderExecutionCapabilities.LoadBalancing
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

        var diagnostics = await _configurationApplier.ApplyConfigurationAsync(
            context.Resource,
            context,
            cancellationToken);

        return diagnostics.Any(diagnostic =>
            diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error)
            ? ProviderExecutionResult.Failed(request, diagnostics)
            : ProviderExecutionResult.Succeeded(request, diagnostics: diagnostics);
    }
}
