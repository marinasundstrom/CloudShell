namespace CloudShell.Abstractions.ResourceManager;

public static class ResourceSettingDisplay
{
    private static readonly string[] SensitiveLiteralNameTokens =
    [
        "apikey",
        "api_key",
        "clientsecret",
        "client_secret",
        "connectionstring",
        "credential",
        "password",
        "secret",
        "token"
    ];

    public static string Format(AppSetting setting)
    {
        ArgumentNullException.ThrowIfNull(setting);

        if (setting.ConfigurationSetting is not null)
        {
            return Format(setting.ConfigurationSetting);
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

        if (assignment.ConfigurationSetting is not null)
        {
            return Format(assignment.ConfigurationSetting);
        }

        if (assignment.Secret is not null)
        {
            return Format(assignment.Secret);
        }

        return assignment.Value;
    }

    public static string Format(ConfigurationSettingReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        return AppendVersion(
            $"@CloudShell.Configuration(storeResourceId={reference.StoreResourceId}; settingName={reference.SettingName}",
            reference.Version);
    }

    public static string Format(SecretReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        return AppendVersion(
            $"@CloudShell.Secret(vaultResourceId={reference.VaultResourceId}; secretName={reference.SecretName}",
            reference.Version);
    }

    public static string Format(CertificateReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        return AppendVersion(
            $"@CloudShell.Certificate(vaultResourceId={reference.VaultResourceId}; certificateName={reference.CertificateName}",
            reference.Version);
    }

    public static bool IsSensitiveLiteralName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var normalizedName = name
            .Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);
        return SensitiveLiteralNameTokens.Any(token =>
            normalizedName.Contains(token.Replace("_", string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase) ||
            name.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static string AppendVersion(string value, string? version) =>
        string.IsNullOrWhiteSpace(version)
            ? $"{value})"
            : $"{value}; version={version})";
}
