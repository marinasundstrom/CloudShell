namespace CloudShell.ControlPlane.Providers;

public sealed class LocalHostNetworkResource(
    Resource resource) : IResourceProjection
{
    public Resource Resource { get; } = resource;

    public string? InfrastructureKind =>
        Resource.Attributes.GetString(LocalHostNetworkResourceTypeProvider.Attributes.InfrastructureKind);

    public string? HostReadiness =>
        Resource.Attributes.GetString(LocalHostNetworkResourceTypeProvider.Attributes.HostReadiness);

    public string? HostOperatingSystem =>
        Resource.Attributes.GetString(LocalHostNetworkResourceTypeProvider.Attributes.HostOperatingSystem);

    public string? NetworkingMode =>
        Resource.Attributes.GetString(LocalHostNetworkResourceTypeProvider.Attributes.NetworkingMode);

    public bool SupportsEndpointMapping =>
        Resource.Capabilities.Has(LocalHostNetworkResourceTypeProvider.Capabilities.NetworkingEndpointMapper);

    public bool SupportsHostNetwork =>
        Resource.Capabilities.Has(LocalHostNetworkResourceTypeProvider.Capabilities.NetworkingHostNetwork);

    public ValueTask<LocalHostNetworkReconcileEndpointMappingsOperation?> GetReconcileEndpointMappingsOperationAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(
            Resource.Operations.Get(LocalHostNetworkResourceTypeProvider.Operations.ReconcileEndpointMappings)
                as LocalHostNetworkReconcileEndpointMappingsOperation);
}

public sealed class LocalHostNetworkResourceProjectionProvider : IResourceProjectionProvider
{
    public ResourceTypeId TypeId => LocalHostNetworkResourceTypeProvider.ResourceTypeId;

    public bool CanProject(Resource resource) =>
        resource.Type.TypeId == LocalHostNetworkResourceTypeProvider.ResourceTypeId;

    public ValueTask<IResourceProjection> ProjectAsync(
        Resource resource,
        ResourceProjectionContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IResourceProjection>(
            new LocalHostNetworkResource(resource));
}
