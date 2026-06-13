namespace CloudShell.Abstractions.ResourceManager;

public static class ResourceSettingDisplay
{
    public static string Format(AppSetting setting)
    {
        ArgumentNullException.ThrowIfNull(setting);

        if (setting.ConfigurationEntry is not null)
        {
            return Format(setting.ConfigurationEntry);
        }

        if (setting.Secret is not null)
        {
            return Format(setting.Secret);
        }

        return setting.Value ?? string.Empty;
    }

    public static string Format(EnvironmentVariableAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);

        if (assignment.ConfigurationEntry is not null)
        {
            return Format(assignment.ConfigurationEntry);
        }

        if (assignment.Secret is not null)
        {
            return Format(assignment.Secret);
        }

        return assignment.Value;
    }

    public static string Format(ConfigurationEntryReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        return AppendVersion(
            $"@CloudShell.Configuration(storeResourceId={reference.StoreResourceId}; entryName={reference.EntryName}",
            reference.Version);
    }

    public static string Format(SecretReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        return AppendVersion(
            $"@CloudShell.Secret(vaultResourceId={reference.VaultResourceId}; secretName={reference.SecretName}",
            reference.Version);
    }

    private static string AppendVersion(string value, string? version) =>
        string.IsNullOrWhiteSpace(version)
            ? $"{value})"
            : $"{value}; version={version})";
}
