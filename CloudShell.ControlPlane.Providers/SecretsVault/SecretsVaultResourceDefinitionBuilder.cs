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
}

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
