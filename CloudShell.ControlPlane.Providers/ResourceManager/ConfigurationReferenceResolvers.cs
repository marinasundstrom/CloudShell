using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.Providers;

public sealed class ConfigurationStoreRuntimeEntryReferenceResolver(
    ConfigurationStoreRuntimeOptions? options = null) :
    IConfigurationEntryReferenceResolver
{
    private readonly ConfigurationStoreRuntimeOptions _options =
        options ?? new ConfigurationStoreRuntimeOptions();

    public ResourceSettingResolutionResult ResolveConfigurationEntry(
        ConfigurationEntryReference reference,
        ResourceSettingResolutionContext context)
    {
        ArgumentNullException.ThrowIfNull(reference);

        if (!string.IsNullOrWhiteSpace(reference.Version))
        {
            return ResourceSettingResolutionResult.Failed(
                $"Configuration entry '{reference.EntryName}' from '{reference.StoreResourceId}' requested version '{reference.Version}', but versioned configuration entries are not supported by the graph runtime resolver.");
        }

        var entry = _options.Entries.FirstOrDefault(entry =>
            string.Equals(entry.Name, reference.EntryName, StringComparison.OrdinalIgnoreCase));
        return entry is null
            ? ResourceSettingResolutionResult.Failed(
                $"Configuration entry '{reference.EntryName}' from '{reference.StoreResourceId}' was not found.")
            : ResourceSettingResolutionResult.Resolved(entry.Value);
    }
}

public sealed class SecretsVaultRuntimeSecretReferenceResolver(
    SecretsVaultRuntimeOptions? options = null) :
    ISecretReferenceResolver,
    ICertificateReferenceResolver
{
    private readonly SecretsVaultRuntimeOptions _options =
        options ?? new SecretsVaultRuntimeOptions();

    public ValueTask<ResourceSettingResolutionResult> ResolveSecretAsync(
        SecretReference reference,
        ResourceSettingResolutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        cancellationToken.ThrowIfCancellationRequested();

        var secret = _options.Secrets
            .Where(secret => string.Equals(secret.Name, reference.SecretName, StringComparison.OrdinalIgnoreCase))
            .Where(secret => string.IsNullOrWhiteSpace(reference.Version) ||
                string.Equals(secret.Version, reference.Version, StringComparison.OrdinalIgnoreCase))
            .LastOrDefault();
        return ValueTask.FromResult(secret is null
            ? ResourceSettingResolutionResult.Failed(
                CreateMissingSecretMessage(reference))
            : ResourceSettingResolutionResult.Resolved(secret.Value));
    }

    public ValueTask<CertificateResolutionResult> ResolveCertificateAsync(
        CertificateReference reference,
        ResourceSettingResolutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        cancellationToken.ThrowIfCancellationRequested();

        var certificate = _options.Certificates
            .Where(certificate => string.Equals(
                certificate.Name,
                reference.CertificateName,
                StringComparison.OrdinalIgnoreCase))
            .Where(certificate => string.IsNullOrWhiteSpace(reference.Version) ||
                string.Equals(certificate.Version, reference.Version, StringComparison.OrdinalIgnoreCase))
            .LastOrDefault();
        return ValueTask.FromResult(certificate is null
            ? CertificateResolutionResult.Failed(
                CreateMissingCertificateMessage(reference))
            : CertificateResolutionResult.Resolved(
                certificate.Value,
                certificate.ContentType,
                certificate.Thumbprint,
                certificate.Subject,
                certificate.NotBefore,
                certificate.Expires));
    }

    private static string CreateMissingSecretMessage(SecretReference reference) =>
        string.IsNullOrWhiteSpace(reference.Version)
            ? $"Secret '{reference.SecretName}' from '{reference.VaultResourceId}' was not found."
            : $"Secret '{reference.SecretName}' version '{reference.Version}' from '{reference.VaultResourceId}' was not found.";

    private static string CreateMissingCertificateMessage(CertificateReference reference) =>
        string.IsNullOrWhiteSpace(reference.Version)
            ? $"Certificate '{reference.CertificateName}' from '{reference.VaultResourceId}' was not found."
            : $"Certificate '{reference.CertificateName}' version '{reference.Version}' from '{reference.VaultResourceId}' was not found.";
}
