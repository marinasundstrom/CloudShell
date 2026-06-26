namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class SecretsVaultResourceDefinitionBuilder(string name) :
    ResourceDefinitionBuilder<SecretsVaultResourceDefinitionBuilder>(name)
{
    protected override ResourceTypeId TypeId =>
        SecretsVaultResourceTypeProvider.ResourceTypeId;

    protected override string? ProviderId =>
        SecretsVaultResourceTypeProvider.ProviderId;

    public SecretsVaultResourceDefinitionBuilder WithEndpoint(string endpoint) =>
        SetScalarAttribute(SecretsVaultResourceTypeProvider.Attributes.Endpoint, endpoint);
}

public static class SecretsVaultResourceDefinitionBuilderExtensions
{
    public static SecretsVaultResourceDefinitionBuilder AddSecretsVault(
        this ResourceDefinitionGraphBuilder graph,
        string name)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var builder = new SecretsVaultResourceDefinitionBuilder(name);
        graph.Add(builder);
        return builder;
    }
}
