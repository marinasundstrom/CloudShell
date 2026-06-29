namespace CloudShell.ResourceModel.ReferenceProviders;

public sealed class DockerHostResource(
    Resource resource) : IResourceProjection
{
    public Resource Resource { get; } = resource;

    public string? HostKind =>
        Resource.Attributes.GetString(DockerHostResourceTypeProvider.Attributes.HostKind);

    public string? Endpoint =>
        Resource.Attributes.GetString(DockerHostResourceTypeProvider.Attributes.Endpoint);

    public string? Registry =>
        Resource.Attributes.GetString(DockerHostResourceTypeProvider.Attributes.Registry);

    public bool IsDefault =>
        bool.TryParse(
            Resource.Attributes.GetString(DockerHostResourceTypeProvider.Attributes.IsDefault),
            out var isDefault) &&
        isDefault;

    public bool SupportsContainerImages =>
        Resource.Capabilities.Has(DockerHostResourceTypeProvider.Capabilities.ContainerImage);

    public bool SupportsContainerBuild =>
        Resource.Capabilities.Has(DockerHostResourceTypeProvider.Capabilities.ContainerBuild);

    public bool SupportsFileSystemMounts =>
        Resource.Capabilities.Has(DockerHostResourceTypeProvider.Capabilities.StorageMountFileSystem);

    public ValueTask<DockerHostInspectOperation?> GetInspectOperationAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(
            Resource.Operations.Get(DockerHostResourceTypeProvider.Operations.Inspect)
                as DockerHostInspectOperation);
}

public sealed class DockerHostResourceProjectionProvider : IResourceProjectionProvider
{
    public ResourceTypeId TypeId => DockerHostResourceTypeProvider.ResourceTypeId;

    public bool CanProject(Resource resource) =>
        resource.Type.TypeId == DockerHostResourceTypeProvider.ResourceTypeId;

    public ValueTask<IResourceProjection> ProjectAsync(
        Resource resource,
        ResourceProjectionContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IResourceProjection>(
            new DockerHostResource(resource));
}
