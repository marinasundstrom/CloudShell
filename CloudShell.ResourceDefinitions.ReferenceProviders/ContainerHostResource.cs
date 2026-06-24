namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class ContainerHostResource(
    Resource resource) : IResourceProjection
{
    public Resource Resource { get; } = resource;

    public string? HostKind =>
        Resource.Attributes.GetString(ContainerHostResourceTypeProvider.Attributes.HostKind);

    public string? Endpoint =>
        Resource.Attributes.GetString(ContainerHostResourceTypeProvider.Attributes.Endpoint);

    public string? Registry =>
        Resource.Attributes.GetString(ContainerHostResourceTypeProvider.Attributes.Registry);

    public bool IsDefault =>
        bool.TryParse(
            Resource.Attributes.GetString(ContainerHostResourceTypeProvider.Attributes.IsDefault),
            out var isDefault) &&
        isDefault;

    public bool SupportsContainerImages =>
        Resource.Capabilities.Has(ContainerHostResourceTypeProvider.Capabilities.ContainerImage);

    public bool SupportsContainerBuild =>
        Resource.Capabilities.Has(ContainerHostResourceTypeProvider.Capabilities.ContainerBuild);

    public bool SupportsFileSystemMounts =>
        Resource.Capabilities.Has(ContainerHostResourceTypeProvider.Capabilities.StorageMountFileSystem);

    public ValueTask<ContainerHostInspectOperation?> GetInspectOperationAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(
            Resource.Operations.Get(ContainerHostResourceTypeProvider.Operations.Inspect)
                as ContainerHostInspectOperation);
}

public sealed class ContainerHostResourceProjectionProvider : IResourceProjectionProvider
{
    public ResourceTypeId TypeId => ContainerHostResourceTypeProvider.ResourceTypeId;

    public bool CanProject(Resource resource) =>
        resource.Type.TypeId == ContainerHostResourceTypeProvider.ResourceTypeId;

    public ValueTask<IResourceProjection> ProjectAsync(
        Resource resource,
        ResourceProjectionContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IResourceProjection>(
            new ContainerHostResource(resource));
}
