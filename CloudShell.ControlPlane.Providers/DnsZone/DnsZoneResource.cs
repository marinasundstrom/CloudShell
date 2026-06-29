namespace CloudShell.ControlPlane.Providers;

public sealed class DnsZoneResource(
    Resource resource) : IResourceProjection
{
    public Resource Resource { get; } = resource;

    public string? ZoneName =>
        Resource.Attributes.GetString(DnsZoneResourceTypeProvider.Attributes.ZoneName);

    public string? Provider =>
        Resource.Attributes.GetString(DnsZoneResourceTypeProvider.Attributes.Provider);

    public bool SupportsNameMapping =>
        Resource.Capabilities.Has(DnsZoneResourceTypeProvider.Capabilities.NetworkingDnsZone);

    public ValueTask<DnsZoneReconcileNameMappingsOperation?> GetReconcileNameMappingsOperationAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(
            Resource.Operations.Get(DnsZoneResourceTypeProvider.Operations.ReconcileNameMappings)
                as DnsZoneReconcileNameMappingsOperation);
}

public sealed class DnsZoneResourceProjectionProvider : IResourceProjectionProvider
{
    public ResourceTypeId TypeId => DnsZoneResourceTypeProvider.ResourceTypeId;

    public bool CanProject(Resource resource) =>
        resource.Type.TypeId == DnsZoneResourceTypeProvider.ResourceTypeId;

    public ValueTask<IResourceProjection> ProjectAsync(
        Resource resource,
        ResourceProjectionContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IResourceProjection>(
            new DnsZoneResource(resource));
}
