namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class ServiceResource(
    Resource resource) : IResourceProjection
{
    public Resource Resource { get; } = resource;

    public string? ServiceKind =>
        Resource.Attributes.GetString(ServiceResourceTypeProvider.Attributes.ServiceKind);

    public string? RoutingMode =>
        Resource.Attributes.GetString(ServiceResourceTypeProvider.Attributes.RoutingMode);

    public IReadOnlyList<ResourceReference> References =>
        Resource.State.ResourceDependencies;

    public bool SupportsEndpointSource =>
        Resource.Capabilities.Has(ServiceResourceTypeProvider.Capabilities.EndpointSource);

    public ValueTask<ServiceReconcileOperation?> GetReconcileOperationAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(
            Resource.Operations.Get(ServiceResourceTypeProvider.Operations.Reconcile)
                as ServiceReconcileOperation);
}

public sealed class ServiceResourceProjectionProvider : IResourceProjectionProvider
{
    public ResourceTypeId TypeId => ServiceResourceTypeProvider.ResourceTypeId;

    public bool CanProject(Resource resource) =>
        resource.Type.TypeId == ServiceResourceTypeProvider.ResourceTypeId;

    public ValueTask<IResourceProjection> ProjectAsync(
        Resource resource,
        ResourceProjectionContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IResourceProjection>(
            new ServiceResource(resource));
}
