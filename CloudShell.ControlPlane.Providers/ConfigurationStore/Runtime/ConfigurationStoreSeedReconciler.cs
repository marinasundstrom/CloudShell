using CloudShell.ControlPlane.ResourceModel;

namespace CloudShell.ControlPlane.Providers;

public sealed class ConfigurationStoreSeedReconciler(
    IConfigurationStoreRuntimeEntryManager entries) : IResourceModelGraphApplyReconciler
{
    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileAsync(
        ResourceModelGraphDefinitionApplyReconciliationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var change in context.Changes.AcceptedResources)
        {
            if (!change.ChangeSet.IsNewResource ||
                change.ChangeSet.Resource.Type.TypeId != ConfigurationStoreResourceTypeProvider.ResourceTypeId ||
                !change.ChangeSet.ProposedState.ResourceAttributeValues.ContainsKey(
                    ConfigurationStoreResourceTypeProvider.Attributes.Entries))
            {
                continue;
            }

            var seedEntries = change.ChangeSet.ProposedState.ResourceAttributeValues
                .GetObject<ConfigurationStoreSettingEntry[]>(
                    ConfigurationStoreResourceTypeProvider.Attributes.Entries) ?? [];
            var accepted = change.AcceptedState!;

            await entries.UpdateEntriesAsync(
                new ProviderRuntimeResourceContext(
                    accepted.EffectiveResourceId,
                    accepted.Name,
                    accepted.DisplayName ?? accepted.Name,
                    accepted.ResourceAttributes.GetValueOrDefault(
                        ConfigurationStoreResourceTypeProvider.Attributes.Endpoint)),
                seedEntries
                    .Select(entry => new ConfigurationStoreRuntimeEntry(
                        entry.Name.Trim(),
                        entry.Value))
                    .ToArray(),
                cancellationToken);
        }

        return [];
    }
}
