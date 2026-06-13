using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Configuration;

public sealed record SecretsVaultDefinition
{
    public SecretsVaultDefinition(
        string id,
        string name,
        IReadOnlyList<SecretsVaultSecret>? secrets = null,
        string? endpoint = null,
        IReadOnlyList<ResourceHealthCheck>? healthChecks = null)
    {
        Id = id;
        Name = name;
        Secrets = secrets ?? [];
        Endpoint = endpoint;
        HealthChecks = healthChecks ?? [];
    }

    public string Id { get; init; }

    public string Name { get; init; }

    public IReadOnlyList<SecretsVaultSecret> Secrets { get; init; }

    public string? Endpoint { get; init; }

    public IReadOnlyList<ResourceHealthCheck> HealthChecks { get; init; }
}

public sealed record SecretsVaultSecret(
    string Name,
    string Value,
    string? Version = null);
