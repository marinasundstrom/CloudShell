namespace CloudShell.ControlPlane.Providers;

public sealed class IdentityProvisioningSetupExecutionHandler(
    IIdentityProvisioningSetupHandler? setupHandler = null) : IProviderExecutionHandler
{
    private readonly IIdentityProvisioningSetupHandler _setupHandler =
        setupHandler ?? new NoopIdentityProvisioningSetupHandler();

    public string InstructionType =>
        ProviderExecutionInstructionTypes.IdentityProvisioningSetup;

    public IReadOnlyList<string> Capabilities =>
    [
        ProviderExecutionCapabilities.IdentityProvisioning
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

        var diagnostics = await _setupHandler.SetupAsync(
            context.Resource,
            cancellationToken);

        return diagnostics.Any(diagnostic =>
            diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error)
            ? ProviderExecutionResult.Failed(request, diagnostics)
            : ProviderExecutionResult.Succeeded(request, diagnostics: diagnostics);
    }
}
