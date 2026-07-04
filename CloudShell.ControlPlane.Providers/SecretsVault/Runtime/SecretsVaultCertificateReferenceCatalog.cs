using CloudShell.Abstractions.ResourceManager;
using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;

namespace CloudShell.ControlPlane.Providers;

public sealed class SecretsVaultCertificateReferenceCatalog(
    ISecretsVaultRuntimeSecretManager secretManager) : ICertificateReferenceCatalog
{
    public async ValueTask<IReadOnlyList<CertificateReferenceDescriptor>> ListCertificatesAsync(
        IReadOnlyList<ResourceManagerResource> resources,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resources);

        var descriptors = new List<CertificateReferenceDescriptor>();
        foreach (var vault in resources
                     .Where(resource => string.Equals(
                         resource.EffectiveTypeId,
                         SecretsVaultResourceTypeProvider.ResourceTypeId.ToString(),
                         StringComparison.OrdinalIgnoreCase))
                     .OrderBy(ResourceDisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var certificates = await secretManager.ListCertificatesAsync(
                vault.Id,
                cancellationToken);
            descriptors.AddRange(certificates.Select(certificate => new CertificateReferenceDescriptor(
                vault.Id,
                certificate.Name,
                certificate.Version,
                certificate.ContentType,
                certificate.Thumbprint,
                certificate.Subject,
                certificate.Expires)));
        }

        return descriptors
            .OrderBy(descriptor => descriptor.VaultResourceId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(descriptor => descriptor.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(descriptor => descriptor.Version, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ResourceDisplayName(ResourceManagerResource resource) =>
        resource.DisplayName ?? resource.Name;
}
