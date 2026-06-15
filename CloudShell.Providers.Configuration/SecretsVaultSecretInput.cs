namespace CloudShell.Providers.Configuration;

public sealed class SecretsVaultSecretInput(string? name = null, string? value = null, string? version = null)
{
    public string? Name { get; set; } = name;

    public string? Value { get; set; } = value;

    public string? Version { get; set; } = version;

    public string? CurrentValue { get; init; }

    public bool IsExisting { get; init; }

    public static SecretsVaultSecretInput FromSecret(SecretsVaultSecret secret) =>
        new(secret.Name, null, secret.Version)
        {
            CurrentValue = secret.Value,
            IsExisting = true
        };
}
