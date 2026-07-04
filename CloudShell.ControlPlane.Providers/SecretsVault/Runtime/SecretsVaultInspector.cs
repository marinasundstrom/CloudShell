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
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
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
