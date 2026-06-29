namespace CloudShell.ResourceModel.ReferenceProviders;

public sealed class SecretsVaultResource(
    Resource resource) : IResourceProjection
{
    public Resource Resource { get; } = resource;

    public string? SecretsKind =>
        Resource.Attributes.GetString(SecretsVaultResourceTypeProvider.Attributes.SecretsKind);

    public string? Endpoint =>
        Resource.Attributes.GetString(SecretsVaultResourceTypeProvider.Attributes.Endpoint);

    public int SecretCount =>
        int.TryParse(
            Resource.Attributes.GetString(SecretsVaultResourceTypeProvider.Attributes.SecretCount),
            out var secretCount)
                ? secretCount
                : 0;

    public ValueTask<SecretsVaultInspectOperation?> GetInspectOperationAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(
            Resource.Operations.Get(SecretsVaultResourceTypeProvider.Operations.Inspect)
                as SecretsVaultInspectOperation);
}

public sealed class SecretsVaultResourceProjectionProvider : IResourceProjectionProvider
{
    public ResourceTypeId TypeId => SecretsVaultResourceTypeProvider.ResourceTypeId;

    public bool CanProject(Resource resource) =>
        resource.Type.TypeId == SecretsVaultResourceTypeProvider.ResourceTypeId;

    public ValueTask<IResourceProjection> ProjectAsync(
        Resource resource,
        ResourceProjectionContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IResourceProjection>(
            new SecretsVaultResource(resource));
}
