using CloudShell.ControlPlane.ResourceModel;

namespace CloudShell.ControlPlane.Providers;

public sealed class ConfigurationStoreSeedReconciler(
    IConfigurationStoreRuntimeSettingManager settings) : IResourceModelGraphApplyReconciler
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
                    ConfigurationStoreResourceTypeProvider.Attributes.Settings))
            {
                continue;
            }

            var seedEntries = change.ChangeSet.ProposedState.ResourceAttributeValues
                .GetObject<ConfigurationStoreSeedSetting[]>(
                    ConfigurationStoreResourceTypeProvider.Attributes.Settings) ?? [];
            var accepted = change.AcceptedState!;

            await settings.UpdateSettingsAsync(
                new ProviderRuntimeResourceContext(
                    accepted.EffectiveResourceId,
                    accepted.Name,
                    accepted.DisplayName ?? accepted.Name,
                    accepted.ResourceAttributes.GetValueOrDefault(
                        ConfigurationStoreResourceTypeProvider.Attributes.Endpoint)),
                seedEntries
                    .Select(setting => new ConfigurationStoreRuntimeSetting(
                        setting.Name.Trim(),
                        setting.Value))
                    .ToArray(),
                cancellationToken);
        }

        return [];
    }
}
