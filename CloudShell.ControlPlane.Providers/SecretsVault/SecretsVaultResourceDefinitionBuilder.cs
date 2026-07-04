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

    public SecretsVaultResourceDefinitionBuilder WithSecret(
        string name,
        string value,
        string? version = null) =>
        WithSecret(new SecretsVaultSeedSecret(name, value, version));

    public SecretsVaultResourceDefinitionBuilder WithSecret(
        SecretsVaultSeedSecret secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        var secrets = Attributes.TryGetValue(
                SecretsVaultResourceTypeProvider.Attributes.Secrets,
                out var currentSecrets)
            ? currentSecrets.ToObject<SecretsVaultSeedSecret[]>() ?? []
            : [];
        return SetObjectAttribute(
            SecretsVaultResourceTypeProvider.Attributes.Secrets,
            secrets.Append(secret).ToArray());
    }

    public SecretsVaultResourceDefinitionBuilder WithSecrets(
        IEnumerable<SecretsVaultSeedSecret> secrets)
    {
        ArgumentNullException.ThrowIfNull(secrets);

        return SetObjectAttribute(
            SecretsVaultResourceTypeProvider.Attributes.Secrets,
            secrets.ToArray());
    }

    public SecretsVaultResourceDefinitionBuilder WithCertificate(
        string name,
        string value,
        string? version = null,
        string? contentType = null) =>
        WithCertificate(new SecretsVaultSeedCertificate(
            name,
            value,
            version,
            contentType));

    public SecretsVaultResourceDefinitionBuilder WithCertificate(
        SecretsVaultSeedCertificate certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        var certificates = Attributes.TryGetValue(
                SecretsVaultResourceTypeProvider.Attributes.Certificates,
                out var currentCertificates)
            ? currentCertificates.ToObject<SecretsVaultSeedCertificate[]>() ?? []
            : [];
        return SetObjectAttribute(
            SecretsVaultResourceTypeProvider.Attributes.Certificates,
            certificates.Append(certificate).ToArray());
    }

    public SecretsVaultResourceDefinitionBuilder WithCertificates(
        IEnumerable<SecretsVaultSeedCertificate> certificates)
    {
        ArgumentNullException.ThrowIfNull(certificates);

        return SetObjectAttribute(
            SecretsVaultResourceTypeProvider.Attributes.Certificates,
            certificates.ToArray());
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
