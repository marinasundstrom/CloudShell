namespace CloudShell.SecretsVaultService;

public sealed record SecretsVaultDefinition
{
    public string Id { get; init; } = string.Empty;

    public IReadOnlyList<SecretsVaultSecret> Secrets { get; init; } = [];
}

public sealed record SecretsVaultSecret(
    string Name,
    string Value,
    string? Version = null);
