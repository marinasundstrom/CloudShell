namespace CloudShell.Providers.Configuration;

public sealed record SecretsVaultDefinition
{
    public SecretsVaultDefinition(
        string id,
        string name,
        IReadOnlyList<SecretsVaultSecret>? secrets = null)
    {
        Id = id;
        Name = name;
        Secrets = secrets ?? [];
    }

    public string Id { get; init; }

    public string Name { get; init; }

    public IReadOnlyList<SecretsVaultSecret> Secrets { get; init; }
}

public sealed record SecretsVaultSecret(
    string Name,
    string Value,
    string? Version = null);
