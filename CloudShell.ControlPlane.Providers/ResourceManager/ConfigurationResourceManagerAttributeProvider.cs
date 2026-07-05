using System.Globalization;
using CloudShell.ControlPlane.ResourceModel;
using ResourceModelResource = CloudShell.ResourceModel.Resource;

namespace CloudShell.ControlPlane.Providers;

public sealed class ConfigurationStoreResourceManagerAttributeProvider(
    ConfigurationStoreRuntimeOptions? options = null) : IResourceModelResourceManagerAttributeProvider
{
    private readonly ConfigurationStoreRuntimeOptions _options =
        options ?? new ConfigurationStoreRuntimeOptions();

    public IReadOnlyDictionary<string, string>? GetAttributes(ResourceModelResource resource) =>
        resource.Type.TypeId == ConfigurationStoreResourceTypeProvider.ResourceTypeId
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ConfigurationStoreResourceTypeProvider.Attributes.SettingCount.ToString()] =
                    _options.Settings.Count.ToString(CultureInfo.InvariantCulture)
            }
            : null;
}

public sealed class SecretsVaultResourceManagerAttributeProvider(
    SecretsVaultRuntimeOptions? options = null) : IResourceModelResourceManagerAttributeProvider
{
    private readonly SecretsVaultRuntimeOptions _options =
        options ?? new SecretsVaultRuntimeOptions();

    public IReadOnlyDictionary<string, string>? GetAttributes(ResourceModelResource resource) =>
        resource.Type.TypeId == SecretsVaultResourceTypeProvider.ResourceTypeId
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [SecretsVaultResourceTypeProvider.Attributes.SecretCount.ToString()] =
                    _options.Secrets.Count.ToString(CultureInfo.InvariantCulture),
                [SecretsVaultResourceTypeProvider.Attributes.CertificateCount.ToString()] =
                    _options.Certificates.Count.ToString(CultureInfo.InvariantCulture)
            }
            : null;
}
