using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ResourceDefinitions.ReferenceProviders.ResourceManager;

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
    ISecretReferenceResolver
{
    private readonly SecretsVaultRuntimeOptions _options =
        options ?? new SecretsVaultRuntimeOptions();

    public ValueTask<ResourceSettingResolutionResult> ResolveSecretAsync(
        SecretReference reference,
        ResourceSettingResolutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);

        if (!string.IsNullOrWhiteSpace(reference.Version))
        {
            return ValueTask.FromResult(ResourceSettingResolutionResult.Failed(
                $"Secret '{reference.SecretName}' from '{reference.VaultResourceId}' requested version '{reference.Version}', but versioned secrets are not supported by the graph runtime resolver."));
        }

        var secret = _options.Secrets.FirstOrDefault(secret =>
            string.Equals(secret.Name, reference.SecretName, StringComparison.OrdinalIgnoreCase));
        return ValueTask.FromResult(secret is null
            ? ResourceSettingResolutionResult.Failed(
                $"Secret '{reference.SecretName}' from '{reference.VaultResourceId}' was not found.")
            : ResourceSettingResolutionResult.Resolved(secret.Value));
    }
}
