namespace CloudShell.ResourceModel.ReferenceProviders;

public sealed class StorageResource(
    Resource resource) : IResourceProjection
{
    public Resource Resource { get; } = resource;

    public string? StorageKind =>
        Resource.Attributes.GetString(StorageResourceTypeProvider.Attributes.StorageKind);

    public string? Provider =>
        Resource.Attributes.GetString(StorageResourceTypeProvider.Attributes.Provider);

    public string? Medium =>
        Resource.Attributes.GetString(StorageResourceTypeProvider.Attributes.Medium);

    public string? Location =>
        Resource.Attributes.GetString(StorageResourceTypeProvider.Attributes.Location);

    public bool SupportsStorage =>
        Resource.Capabilities.Has(StorageResourceTypeProvider.Capabilities.StorageProvider);

    public bool SupportsMounts =>
        Resource.Capabilities.Has(StorageResourceTypeProvider.Capabilities.StorageMountProvider);

    public ValueTask<StorageInspectOperation?> GetInspectOperationAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(
            Resource.Operations.Get(StorageResourceTypeProvider.Operations.Inspect)
                as StorageInspectOperation);
}

public sealed class StorageResourceProjectionProvider : IResourceProjectionProvider
{
    public ResourceTypeId TypeId => StorageResourceTypeProvider.ResourceTypeId;

    public bool CanProject(Resource resource) =>
        resource.Type.TypeId == StorageResourceTypeProvider.ResourceTypeId;

    public ValueTask<IResourceProjection> ProjectAsync(
        Resource resource,
        ResourceProjectionContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IResourceProjection>(
            new StorageResource(resource));
}
