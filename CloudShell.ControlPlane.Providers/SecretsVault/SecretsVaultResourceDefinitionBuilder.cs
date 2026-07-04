namespace CloudShell.ControlPlane.Providers;

public sealed class SecretsVaultResourceDefinitionBuilder(string name) :
    ResourceDefinitionBuilder<SecretsVaultResourceDefinitionBuilder>(name)
{
    protected override ResourceTypeId TypeId =>
        SecretsVaultResourceTypeProvider.ResourceTypeId;

    protected override string? ProviderId =>
        SecretsVaultResourceTypeProvider.ProviderId;

    public SecretsVaultResourceDefinitionBuilder WithRuntimeMonitoring() =>
        this;

    public SecretsVaultResourceDefinitionBuilder WithEndpoint(string endpoint) =>
        SetScalarAttribute(SecretsVaultResourceTypeProvider.Attributes.Endpoint, endpoint);

    public SecretsVaultResourceDefinitionBuilder WithSeed(
        Action<SecretsVaultSeedBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var seed = new SecretsVaultSeedBuilder();
        configure(seed);
        if (seed.Secrets.Count > 0)
        {
            SetObjectAttribute(
                SecretsVaultResourceTypeProvider.Attributes.Secrets,
                seed.Secrets);
        }

        if (seed.Certificates.Count > 0)
        {
            SetObjectAttribute(
                SecretsVaultResourceTypeProvider.Attributes.Certificates,
                seed.Certificates);
        }

        return this;
    }

    public ResourceSecretReference Secret(
        string name,
        string? version = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new(
            EffectiveResourceId,
            name.Trim(),
            string.IsNullOrWhiteSpace(version) ? null : version.Trim());
    }

    public ResourceCertificateReference Certificate(
        string name,
        string? version = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new(
            EffectiveResourceId,
            name.Trim(),
            string.IsNullOrWhiteSpace(version) ? null : version.Trim());
    }
}

public sealed class SecretsVaultSeedBuilder
{
    private readonly List<SecretsVaultSeedSecret> _secrets = [];
    private readonly List<SecretsVaultSeedCertificate> _certificates = [];

    public IReadOnlyList<SecretsVaultSeedSecret> Secrets => _secrets;

    public IReadOnlyList<SecretsVaultSeedCertificate> Certificates => _certificates;

    public SecretsVaultSeedBuilder Secret(
        string name,
        string value,
        string? version = null)
    {
        _secrets.Add(new(name, value, version));
        return this;
    }

    public SecretsVaultSeedBuilder Secret(
        SecretsVaultSeedSecret secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        _secrets.Add(secret);
        return this;
    }

    public SecretsVaultSeedBuilder Certificate(
        string name,
        string value,
        string? version = null,
        string? contentType = null)
    {
        _certificates.Add(new(
            name,
            value,
            version,
            contentType));
        return this;
    }

    public SecretsVaultSeedBuilder Certificate(
        SecretsVaultSeedCertificate certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        _certificates.Add(certificate);
        return this;
    }
}

public sealed record SecretsVaultSeedSecret(
    string Name,
    string Value,
    string? Version = null);

public sealed record SecretsVaultSeedCertificate(
    string Name,
    string Value,
    string? Version = null,
    string? ContentType = null,
    string? Thumbprint = null,
    string? Subject = null,
    DateTimeOffset? NotBefore = null,
    DateTimeOffset? Expires = null,
    bool? HasPrivateKey = null);

public sealed record ResourceCertificateReference(
    string VaultResourceId,
    string Name,
    string? Version = null);

public static class SecretsVaultResourceDefinitionBuilderExtensions
{
    public static SecretsVaultResourceDefinitionBuilder AddSecretsVault(
        this ResourceGraphBuilder graph,
        string name)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var builder = new SecretsVaultResourceDefinitionBuilder(name)
            .WithRuntimeMonitoring();
        graph.Add(builder);
        return builder;
    }
}
