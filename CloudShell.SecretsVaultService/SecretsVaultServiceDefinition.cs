namespace CloudShell.SecretsVaultService;

public sealed record SecretsVaultDefinition
{
    public string Id { get; init; } = string.Empty;

    public IReadOnlyList<SecretsVaultSecret> Secrets { get; init; } = [];

    public IReadOnlyList<SecretsVaultCertificate> Certificates { get; init; } = [];
}

public sealed record SecretsVaultSecret(
    string Name,
    string Value,
    string? Version = null);

public sealed record SecretsVaultCertificate(
    string Name,
    string Value,
    string? Version = null,
    string? ContentType = null,
    string? Thumbprint = null,
    string? Subject = null,
    DateTimeOffset? NotBefore = null,
    DateTimeOffset? Expires = null,
    bool? HasPrivateKey = null);
