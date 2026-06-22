using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

internal static class ApplicationConfigurationReferences
{
    public static IReadOnlyList<AppSetting> NormalizeAppSettings(
        IReadOnlyList<AppSetting> appSettings) =>
        appSettings
            .Where(setting => !string.IsNullOrWhiteSpace(setting.Name))
            .Select(setting => setting with
            {
                Name = setting.Name.Trim(),
                ConfigurationEntry = NormalizeConfigurationEntryReference(setting.ConfigurationEntry),
                Secret = NormalizeSecretReference(setting.Secret)
            })
            .Where(setting => setting.Value is not null ||
                setting.ConfigurationEntry is not null ||
                setting.Secret is not null)
            .ToArray();

    public static IReadOnlyList<EnvironmentVariableAssignment> NormalizeEnvironmentVariables(
        IReadOnlyList<EnvironmentVariableAssignment> environmentVariables) =>
        environmentVariables
            .Where(variable => !string.IsNullOrWhiteSpace(variable.Name))
            .Select(variable => variable with
            {
                Name = variable.Name.Trim(),
                ConfigurationEntry = NormalizeConfigurationEntryReference(variable.ConfigurationEntry),
                Secret = NormalizeSecretReference(variable.Secret)
            })
            .Where(variable => variable.ConfigurationEntry is null || variable.Secret is null)
            .ToArray();

    public static IEnumerable<string> GetDependencyResourceIds(
        IReadOnlyList<string> existingDependencies,
        IReadOnlyList<AppSetting> appSettings,
        IReadOnlyList<EnvironmentVariableAssignment> environmentVariables) =>
        existingDependencies
            .Concat(GetAppSettingReferenceResourceIds(appSettings))
            .Concat(GetEnvironmentVariableReferenceResourceIds(environmentVariables))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> GetAppSettingReferenceResourceIds(IReadOnlyList<AppSetting> appSettings)
    {
        foreach (var setting in appSettings)
        {
            if (!string.IsNullOrWhiteSpace(setting.ConfigurationEntry?.StoreResourceId))
            {
                yield return setting.ConfigurationEntry.StoreResourceId;
            }

            if (!string.IsNullOrWhiteSpace(setting.Secret?.VaultResourceId))
            {
                yield return setting.Secret.VaultResourceId;
            }
        }
    }

    private static IEnumerable<string> GetEnvironmentVariableReferenceResourceIds(
        IReadOnlyList<EnvironmentVariableAssignment> environmentVariables)
    {
        foreach (var variable in environmentVariables)
        {
            if (!string.IsNullOrWhiteSpace(variable.ConfigurationEntry?.StoreResourceId))
            {
                yield return variable.ConfigurationEntry.StoreResourceId;
            }

            if (!string.IsNullOrWhiteSpace(variable.Secret?.VaultResourceId))
            {
                yield return variable.Secret.VaultResourceId;
            }
        }
    }

    private static ConfigurationEntryReference? NormalizeConfigurationEntryReference(
        ConfigurationEntryReference? reference) =>
        reference is null ||
        string.IsNullOrWhiteSpace(reference.StoreResourceId) ||
        string.IsNullOrWhiteSpace(reference.EntryName)
            ? null
            : reference with
            {
                StoreResourceId = reference.StoreResourceId.Trim(),
                EntryName = reference.EntryName.Trim(),
                Version = NormalizeNullable(reference.Version)
            };

    private static SecretReference? NormalizeSecretReference(SecretReference? reference) =>
        reference is null ||
        string.IsNullOrWhiteSpace(reference.VaultResourceId) ||
        string.IsNullOrWhiteSpace(reference.SecretName)
            ? null
            : reference with
            {
                VaultResourceId = reference.VaultResourceId.Trim(),
                SecretName = reference.SecretName.Trim(),
                Version = NormalizeNullable(reference.Version)
            };

    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
