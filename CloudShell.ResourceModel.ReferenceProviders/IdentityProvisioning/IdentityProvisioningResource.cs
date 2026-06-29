namespace CloudShell.ResourceModel.ReferenceProviders;

public sealed class IdentityProvisioningResource(
    Resource resource) : IResourceProjection
{
    public Resource Resource { get; } = resource;

    public string? InfrastructureKind =>
        Resource.Attributes.GetString(IdentityProvisioningResourceTypeProvider.Attributes.InfrastructureKind);

    public string? IdentityProvider =>
        Resource.Attributes.GetString(IdentityProvisioningResourceTypeProvider.Attributes.IdentityProvider);

    public string? IdentityProviderId =>
        Resource.Attributes.GetString(IdentityProvisioningResourceTypeProvider.Attributes.IdentityProviderId);

    public string? ProviderKind =>
        Resource.Attributes.GetString(IdentityProvisioningResourceTypeProvider.Attributes.ProviderKind);

    public bool SupportsIdentityProvisioning =>
        Resource.Capabilities.Has(IdentityProvisioningResourceTypeProvider.Capabilities.IdentityProvisioning);

    public ValueTask<IdentityProvisioningSetupOperation?> GetSetupOperationAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(
            Resource.Operations.Get(IdentityProvisioningResourceTypeProvider.Operations.Setup)
                as IdentityProvisioningSetupOperation);
}

public sealed class IdentityProvisioningResourceProjectionProvider : IResourceProjectionProvider
{
    public ResourceTypeId TypeId => IdentityProvisioningResourceTypeProvider.ResourceTypeId;

    public bool CanProject(Resource resource) =>
        resource.Type.TypeId == IdentityProvisioningResourceTypeProvider.ResourceTypeId;

    public ValueTask<IResourceProjection> ProjectAsync(
        Resource resource,
        ResourceProjectionContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IResourceProjection>(
            new IdentityProvisioningResource(resource));
}
