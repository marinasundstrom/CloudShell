namespace CloudShell.ControlPlane.Providers;

public sealed class NetworkResource(
    Resource resource) : IResourceProjection
{
    public Resource Resource { get; } = resource;

    public string? NetworkKind =>
        Resource.Attributes.GetString(NetworkResourceTypeProvider.Attributes.NetworkKind);

    public string? HostReadiness =>
        Resource.Attributes.GetString(NetworkResourceTypeProvider.Attributes.HostReadiness);

    public string? MappingProviders =>
        Resource.Attributes.GetString(NetworkResourceTypeProvider.Attributes.MappingProviders);

    public bool SupportsEndpointMapping =>
        Resource.Capabilities.Has(NetworkResourceTypeProvider.Capabilities.NetworkingEndpointMapper);

    public ValueTask<NetworkReconcileEndpointMappingsOperation?> GetReconcileEndpointMappingsOperationAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(
            Resource.Operations.Get(NetworkResourceTypeProvider.Operations.ReconcileEndpointMappings)
                as NetworkReconcileEndpointMappingsOperation);
}

public sealed class NetworkResourceProjectionProvider : IResourceProjectionProvider
{
    public ResourceTypeId TypeId => NetworkResourceTypeProvider.ResourceTypeId;

    public bool CanProject(Resource resource) =>
        resource.Type.TypeId == NetworkResourceTypeProvider.ResourceTypeId;

    public ValueTask<IResourceProjection> ProjectAsync(
        Resource resource,
        ResourceProjectionContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IResourceProjection>(
            new NetworkResource(resource));
}
