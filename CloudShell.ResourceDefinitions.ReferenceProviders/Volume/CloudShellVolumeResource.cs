namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class CloudShellVolumeResource(
    Resource resource) : IResourceProjection
{
    public Resource Resource { get; } = resource;

    public string? StorageKind =>
        Resource.Attributes.GetString(CloudShellVolumeResourceTypeProvider.Attributes.StorageKind);

    public string? Provider =>
        Resource.Attributes.GetString(CloudShellVolumeResourceTypeProvider.Attributes.Provider);

    public string? StorageMedium =>
        Resource.Attributes.GetString(CloudShellVolumeResourceTypeProvider.Attributes.StorageMedium);

    public string? Location =>
        Resource.Attributes.GetString(CloudShellVolumeResourceTypeProvider.Attributes.Location);

    public string? SubPath =>
        Resource.Attributes.GetString(CloudShellVolumeResourceTypeProvider.Attributes.SubPath);

    public string? AccessMode =>
        Resource.Attributes.GetString(CloudShellVolumeResourceTypeProvider.Attributes.AccessMode);

    public bool Persistent =>
        bool.TryParse(
            Resource.Attributes.GetString(CloudShellVolumeResourceTypeProvider.Attributes.Persistent),
            out var persistent) &&
        persistent;

    public IReadOnlyList<ResourceReference> References =>
        Resource.State.ResourceDependencies;

    public bool SupportsVolume =>
        Resource.Capabilities.Has(CloudShellVolumeResourceTypeProvider.Capabilities.StorageVolume);

    public ValueTask<CloudShellVolumeProvisionOperation?> GetProvisionOperationAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(
            Resource.Operations.Get(CloudShellVolumeResourceTypeProvider.Operations.Provision)
                as CloudShellVolumeProvisionOperation);
}

public sealed class CloudShellVolumeResourceProjectionProvider : IResourceProjectionProvider
{
    public ResourceTypeId TypeId => CloudShellVolumeResourceTypeProvider.ResourceTypeId;

    public bool CanProject(Resource resource) =>
        resource.Type.TypeId == CloudShellVolumeResourceTypeProvider.ResourceTypeId;

    public ValueTask<IResourceProjection> ProjectAsync(
        Resource resource,
        ResourceProjectionContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IResourceProjection>(
            new CloudShellVolumeResource(resource));
}
