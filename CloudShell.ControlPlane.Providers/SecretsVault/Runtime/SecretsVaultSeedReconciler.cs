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
                !change.ChangeSet.ProposedState.ResourceAttributeValues.ContainsKey(
                    SecretsVaultResourceTypeProvider.Attributes.Secrets))
            {
                continue;
            }

            var seedSecrets = change.ChangeSet.ProposedState.ResourceAttributeValues
                .GetObject<SecretsVaultSeedSecret[]>(
                    SecretsVaultResourceTypeProvider.Attributes.Secrets) ?? [];
            var accepted = change.AcceptedState!;

            await secrets.UpdateSecretsAsync(
                new ProviderRuntimeResourceContext(
                    accepted.EffectiveResourceId,
                    accepted.Name,
                    accepted.DisplayName ?? accepted.Name,
                    accepted.ResourceAttributes.GetValueOrDefault(
                        SecretsVaultResourceTypeProvider.Attributes.Endpoint)),
                seedSecrets
                    .Select(secret => new SecretsVaultRuntimeSecret(
                        secret.Name.Trim(),
                        secret.Value,
                        string.IsNullOrWhiteSpace(secret.Version) ? null : secret.Version.Trim()))
                    .ToArray(),
                cancellationToken);
        }

        return [];
    }
}
