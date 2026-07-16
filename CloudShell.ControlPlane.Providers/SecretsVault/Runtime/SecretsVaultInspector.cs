namespace CloudShell.ControlPlane.Providers;

public interface ISecretsVaultInspector
{
    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> InspectAsync(
        Resource resource,
        CancellationToken cancellationToken = default);
}

public sealed class NoopSecretsVaultInspector :
    ISecretsVaultInspector
{
    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> InspectAsync(
        Resource resource,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>(
        [
            SecretsVaultInspectorReadiness.CreateMissingDiagnostic(resource)
        ]);
}

internal static class SecretsVaultInspectorReadiness
{
    public const string DiagnosticCode = "secrets.vault.inspectorMissing";

    public static bool IsMissing(ISecretsVaultInspector? inspector) =>
        inspector is null or NoopSecretsVaultInspector;

    public static string CreateMissingReason(Resource resource) =>
        $"Secrets Vault resource '{resource.EffectiveResourceId}' cannot be inspected because no Secrets Vault inspector is registered.";

    public static ResourceDefinitionDiagnostic CreateMissingDiagnostic(Resource resource) =>
        ResourceDefinitionDiagnostic.Error(
            DiagnosticCode,
            CreateMissingReason(resource),
            resource.EffectiveResourceId);
}

public sealed class SecretsVaultRuntimeInspector(
    SecretsVaultRuntimeOptions? options = null) :
    ISecretsVaultInspector
{
    private readonly SecretsVaultRuntimeOptions _options =
        options ?? new SecretsVaultRuntimeOptions();

    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> InspectAsync(
        Resource resource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>(
        [
            new(
                ResourceDefinitionDiagnosticSeverity.Information,
                "secrets.vault.inspect.runtimeSecrets",
                $"Secrets Vault runtime has {_options.Secrets.Count} configured secret{(_options.Secrets.Count == 1 ? string.Empty : "s")} and {_options.Certificates.Count} configured certificate{(_options.Certificates.Count == 1 ? string.Empty : "s")} for '{resource.EffectiveResourceId}'.",
                resource.EffectiveResourceId)
        ]);
    }
}
