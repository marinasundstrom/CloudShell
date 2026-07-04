using CloudShell.ControlPlane.ResourceModel;

namespace CloudShell.ControlPlane.Providers;

public sealed class SecretsVaultSeedReconciler(
    ISecretsVaultRuntimeSecretManager secrets) : IResourceModelGraphApplyReconciler
{
    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileAsync(
        ResourceModelGraphDefinitionApplyReconciliationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var change in context.Changes.AcceptedResources)
        {
            if (!change.ChangeSet.IsNewResource ||
                change.ChangeSet.Resource.Type.TypeId != SecretsVaultResourceTypeProvider.ResourceTypeId ||
                (!change.ChangeSet.ProposedState.ResourceAttributeValues.ContainsKey(
                    SecretsVaultResourceTypeProvider.Attributes.Secrets) &&
                 !change.ChangeSet.ProposedState.ResourceAttributeValues.ContainsKey(
                    SecretsVaultResourceTypeProvider.Attributes.Certificates)))
            {
                continue;
            }

            var seedSecrets = change.ChangeSet.ProposedState.ResourceAttributeValues
                .GetObject<SecretsVaultSeedSecret[]>(
                    SecretsVaultResourceTypeProvider.Attributes.Secrets) ?? [];
            var seedCertificates = change.ChangeSet.ProposedState.ResourceAttributeValues
                .GetObject<SecretsVaultSeedCertificate[]>(
                    SecretsVaultResourceTypeProvider.Attributes.Certificates) ?? [];
            var accepted = change.AcceptedState!;
            var resource = new ProviderRuntimeResourceContext(
                accepted.EffectiveResourceId,
                accepted.Name,
                accepted.DisplayName ?? accepted.Name,
                accepted.ResourceAttributes.GetValueOrDefault(
                    SecretsVaultResourceTypeProvider.Attributes.Endpoint));

            await secrets.UpdateSecretsAsync(
                resource,
                seedSecrets
                    .Select(secret => new SecretsVaultRuntimeSecret(
                        secret.Name.Trim(),
                        secret.Value,
                        string.IsNullOrWhiteSpace(secret.Version) ? null : secret.Version.Trim()))
                    .ToArray(),
                cancellationToken);

            await secrets.UpdateCertificatesAsync(
                resource,
                seedCertificates
                    .Select(certificate => new SecretsVaultRuntimeCertificate(
                        certificate.Name.Trim(),
                        certificate.Value,
                        string.IsNullOrWhiteSpace(certificate.Version) ? null : certificate.Version.Trim(),
                        string.IsNullOrWhiteSpace(certificate.ContentType) ? null : certificate.ContentType.Trim(),
                        string.IsNullOrWhiteSpace(certificate.Thumbprint) ? null : certificate.Thumbprint.Trim(),
                        string.IsNullOrWhiteSpace(certificate.Subject) ? null : certificate.Subject.Trim(),
                        certificate.NotBefore,
                        certificate.Expires,
                        certificate.HasPrivateKey))
                    .ToArray(),
                cancellationToken);
        }

        return [];
    }
}
