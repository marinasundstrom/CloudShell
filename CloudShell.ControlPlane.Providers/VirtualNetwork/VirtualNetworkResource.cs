namespace CloudShell.ControlPlane.Providers;

public sealed class VirtualNetworkResource(
    Resource resource) : IResourceProjection
{
    public Resource Resource { get; } = resource;

    public string? NetworkKind =>
        Resource.Attributes.GetString(VirtualNetworkResourceTypeProvider.Attributes.NetworkKind);

    public bool IsDefault =>
        bool.TryParse(
            Resource.Attributes.GetString(VirtualNetworkResourceTypeProvider.Attributes.IsDefault),
            out var isDefault) &&
        isDefault;

    public string? HostReadiness =>
        Resource.Attributes.GetString(VirtualNetworkResourceTypeProvider.Attributes.HostReadiness);

    public string? MappingProviders =>
        Resource.Attributes.GetString(VirtualNetworkResourceTypeProvider.Attributes.MappingProviders);

    public bool SupportsEndpointMapping =>
        Resource.Capabilities.Has(VirtualNetworkResourceTypeProvider.Capabilities.NetworkingEndpointMapper);

    public bool SupportsIngress =>
        Resource.Capabilities.Has(VirtualNetworkResourceTypeProvider.Capabilities.NetworkingIngress);

    public ValueTask<VirtualNetworkReconcileEndpointMappingsOperation?> GetReconcileEndpointMappingsOperationAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(
            Resource.Operations.Get(VirtualNetworkResourceTypeProvider.Operations.ReconcileEndpointMappings)
                as VirtualNetworkReconcileEndpointMappingsOperation);
}

public sealed class VirtualNetworkResourceProjectionProvider : IResourceProjectionProvider
{
    public ResourceTypeId TypeId => VirtualNetworkResourceTypeProvider.ResourceTypeId;

    public bool CanProject(Resource resource) =>
        resource.Type.TypeId == VirtualNetworkResourceTypeProvider.ResourceTypeId;

    public ValueTask<IResourceProjection> ProjectAsync(
        Resource resource,
        ResourceProjectionContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IResourceProjection>(
            new VirtualNetworkResource(resource));
}
