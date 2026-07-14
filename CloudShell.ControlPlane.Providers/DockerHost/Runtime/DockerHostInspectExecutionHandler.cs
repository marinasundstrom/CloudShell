namespace CloudShell.ControlPlane.Providers;

public sealed class DockerHostInspectExecutionHandler(
    IDockerHostInspector? inspector = null) : IProviderExecutionHandler
{
    private readonly IDockerHostInspector _inspector =
        inspector ?? new NoopDockerHostInspector();

    public string InstructionType =>
        ProviderExecutionInstructionTypes.DockerHostInspect;

    public IReadOnlyList<string> Capabilities =>
    [
        ProviderExecutionCapabilities.RuntimeObservation
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

        var diagnostics = await _inspector.InspectAsync(
            context.Resource,
            cancellationToken);

        return diagnostics.Any(diagnostic =>
            diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error)
            ? ProviderExecutionResult.Failed(request, diagnostics)
            : ProviderExecutionResult.Succeeded(request, diagnostics: diagnostics);
    }
}
