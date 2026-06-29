namespace CloudShell.ControlPlane.Providers;

public sealed class LoadBalancerResource(
    Resource resource) : IResourceProjection
{
    public Resource Resource { get; } = resource;

    public string? Provider =>
        Resource.Attributes.GetString(LoadBalancerResourceTypeProvider.Attributes.Provider);

    public string? HostResourceId =>
        Resource.Attributes.GetString(LoadBalancerResourceTypeProvider.Attributes.HostResourceId);

    public int EntrypointCount =>
        GetCount(LoadBalancerResourceTypeProvider.Attributes.EntrypointCount);

    public int RouteCount =>
        GetCount(LoadBalancerResourceTypeProvider.Attributes.RouteCount);

    public int HttpRouteCount =>
        GetCount(LoadBalancerResourceTypeProvider.Attributes.HttpRouteCount);

    public int TcpRouteCount =>
        GetCount(LoadBalancerResourceTypeProvider.Attributes.TcpRouteCount);

    public int EndpointCount =>
        GetCount(LoadBalancerResourceTypeProvider.Attributes.EndpointCount);

    public IReadOnlyList<ResourceReference> References =>
        Resource.State.StartupDependencies;

    public bool SupportsLoadBalancing =>
        Resource.Capabilities.Has(LoadBalancerResourceTypeProvider.Capabilities.NetworkingLoadBalancer);

    public ValueTask<LoadBalancerApplyConfigurationOperation?> GetApplyConfigurationOperationAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(
            Resource.Operations.Get(LoadBalancerResourceTypeProvider.Operations.ApplyConfiguration)
                as LoadBalancerApplyConfigurationOperation);

    private int GetCount(ResourceAttributeId attributeId) =>
        int.TryParse(Resource.Attributes.GetString(attributeId), out var count)
            ? count
            : 0;
}

public sealed class LoadBalancerResourceProjectionProvider : IResourceProjectionProvider
{
    public ResourceTypeId TypeId => LoadBalancerResourceTypeProvider.ResourceTypeId;

    public bool CanProject(Resource resource) =>
        resource.Type.TypeId == LoadBalancerResourceTypeProvider.ResourceTypeId;

    public ValueTask<IResourceProjection> ProjectAsync(
        Resource resource,
        ResourceProjectionContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IResourceProjection>(
            new LoadBalancerResource(resource));
}
