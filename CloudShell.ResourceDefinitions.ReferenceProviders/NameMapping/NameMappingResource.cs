namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class NameMappingResource(
    Resource resource) : IResourceProjection
{
    public Resource Resource { get; } = resource;

    public string? HostName =>
        Resource.Attributes.GetString(NameMappingResourceTypeProvider.Attributes.HostName);

    public string? TargetEndpointName =>
        Resource.Attributes.GetString(NameMappingResourceTypeProvider.Attributes.TargetEndpointName);

    public string? Exposure =>
        Resource.Attributes.GetString(NameMappingResourceTypeProvider.Attributes.Exposure);

    public IReadOnlyList<ResourceReference> References =>
        Resource.State.StartupDependencies;

    public bool SupportsNameMapping =>
        Resource.Capabilities.Has(NameMappingResourceTypeProvider.Capabilities.NetworkingNameMapping);
}

public sealed class NameMappingResourceProjectionProvider : IResourceProjectionProvider
{
    public ResourceTypeId TypeId => NameMappingResourceTypeProvider.ResourceTypeId;

    public bool CanProject(Resource resource) =>
        resource.Type.TypeId == NameMappingResourceTypeProvider.ResourceTypeId;

    public ValueTask<IResourceProjection> ProjectAsync(
        Resource resource,
        ResourceProjectionContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IResourceProjection>(
            new NameMappingResource(resource));
}
