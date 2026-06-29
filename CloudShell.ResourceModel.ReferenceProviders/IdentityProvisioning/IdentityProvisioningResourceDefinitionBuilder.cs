namespace CloudShell.ResourceModel.ReferenceProviders;

public sealed class IdentityProvisioningResourceDefinitionBuilder(string name) :
    ResourceDefinitionBuilder<IdentityProvisioningResourceDefinitionBuilder>(name)
{
    protected override ResourceTypeId TypeId =>
        IdentityProvisioningResourceTypeProvider.ResourceTypeId;

    protected override string? ProviderId =>
        IdentityProvisioningResourceTypeProvider.ProviderId;

    public IdentityProvisioningResourceDefinitionBuilder WithIdentityProvider(string provider) =>
        SetScalarAttribute(IdentityProvisioningResourceTypeProvider.Attributes.IdentityProvider, provider);

    public IdentityProvisioningResourceDefinitionBuilder WithIdentityProviderId(string providerId) =>
        SetScalarAttribute(IdentityProvisioningResourceTypeProvider.Attributes.IdentityProviderId, providerId);

    public IdentityProvisioningResourceDefinitionBuilder WithProviderKind(string providerKind) =>
        SetScalarAttribute(IdentityProvisioningResourceTypeProvider.Attributes.ProviderKind, providerKind);
}

public static class IdentityProvisioningResourceDefinitionBuilderExtensions
{
    public static IdentityProvisioningResourceDefinitionBuilder AddIdentityProvisioning(
        this ResourceDefinitionGraphBuilder graph,
        string name)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var builder = new IdentityProvisioningResourceDefinitionBuilder(name);
        graph.Add(builder);
        return builder;
    }
}
