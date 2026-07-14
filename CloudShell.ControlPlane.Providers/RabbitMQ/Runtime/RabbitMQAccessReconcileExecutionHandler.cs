using CloudShell.Abstractions.ResourceManager;
using System.Text.Json;

namespace CloudShell.ControlPlane.Providers;

public sealed class RabbitMQAccessReconcileExecutionHandler(
    IRabbitMQAccessReconciler? accessReconciler = null) : IProviderExecutionHandler
{
    private readonly IRabbitMQAccessReconciler _accessReconciler =
        accessReconciler ?? new NoopRabbitMQAccessReconciler();

    public string InstructionType =>
        ProviderExecutionInstructionTypes.RabbitMQAccessReconcile;

    public IReadOnlyList<string> Capabilities { get; } =
    [
        ProviderExecutionCapabilities.RabbitMQAccess
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

        var payload = request.Payload?.Deserialize<RabbitMQAccessReconcileExecutionPayload>() ??
            new RabbitMQAccessReconcileExecutionPayload([]);
        var diagnostics = await _accessReconciler.ReconcileAccessAsync(
            context.Resource,
            payload.Grants,
            cancellationToken);

        return diagnostics.Any(diagnostic =>
            diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error)
            ? ProviderExecutionResult.Failed(request, diagnostics)
            : ProviderExecutionResult.Succeeded(
                request,
                diagnostics: diagnostics,
                observations: new Dictionary<string, string>
                {
                    ["grantCount"] = payload.Grants.Count.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    ["diagnosticCount"] = diagnostics.Count.ToString(
                        System.Globalization.CultureInfo.InvariantCulture)
                });
    }
}

public sealed record RabbitMQAccessReconcileExecutionPayload(
    IReadOnlyList<ResourcePermissionGrant> Grants);
