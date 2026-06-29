namespace CloudShell.ResourceModel.ReferenceProviders;

public sealed class LocalVolumeResource(
    Resource resource) : IResourceProjection
{
    public Resource Resource { get; } = resource;

    public string? StorageKind =>
        Resource.Attributes.GetString(LocalVolumeResourceTypeProvider.Attributes.StorageKind);

    public string? StorageMedium =>
        Resource.Attributes.GetString(LocalVolumeResourceTypeProvider.Attributes.StorageMedium);

    public ValueTask<LocalVolumeProvisionOperation?> GetProvisionOperationAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Resource.Operations.Get<LocalVolumeProvisionOperation>());
}

public sealed class LocalVolumeResourceProjectionProvider : IResourceProjectionProvider
{
    public ResourceTypeId TypeId => LocalVolumeResourceTypeProvider.ResourceTypeId;

    public bool CanProject(Resource resource) =>
        resource.Type.TypeId == LocalVolumeResourceTypeProvider.ResourceTypeId;

    public ValueTask<IResourceProjection> ProjectAsync(
        Resource resource,
        ResourceProjectionContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IResourceProjection>(
            new LocalVolumeResource(resource));
}
