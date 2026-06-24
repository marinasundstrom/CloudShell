namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class MacOSHostNetworkResource(
    Resource resource) : IResourceProjection
{
    public Resource Resource { get; } = resource;

    public string? InfrastructureKind =>
        Resource.Attributes.GetString(MacOSHostNetworkResourceTypeProvider.Attributes.InfrastructureKind);

    public string? HostReadiness =>
        Resource.Attributes.GetString(MacOSHostNetworkResourceTypeProvider.Attributes.HostReadiness);

    public string? HostOperatingSystem =>
        Resource.Attributes.GetString(MacOSHostNetworkResourceTypeProvider.Attributes.HostOperatingSystem);

    public string? NetworkingMode =>
        Resource.Attributes.GetString(MacOSHostNetworkResourceTypeProvider.Attributes.NetworkingMode);

    public bool SupportsEndpointMapping =>
        Resource.Capabilities.Has(MacOSHostNetworkResourceTypeProvider.Capabilities.NetworkingEndpointMapper);

    public bool SupportsHostNetwork =>
        Resource.Capabilities.Has(MacOSHostNetworkResourceTypeProvider.Capabilities.NetworkingHostNetwork);

    public ValueTask<MacOSHostNetworkReconcileEndpointMappingsOperation?> GetReconcileEndpointMappingsOperationAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(
            Resource.Operations.Get(MacOSHostNetworkResourceTypeProvider.Operations.ReconcileEndpointMappings)
                as MacOSHostNetworkReconcileEndpointMappingsOperation);
}

public sealed class MacOSHostNetworkResourceProjectionProvider : IResourceProjectionProvider
{
    public ResourceTypeId TypeId => MacOSHostNetworkResourceTypeProvider.ResourceTypeId;

    public bool CanProject(Resource resource) =>
        resource.Type.TypeId == MacOSHostNetworkResourceTypeProvider.ResourceTypeId;

    public ValueTask<IResourceProjection> ProjectAsync(
        Resource resource,
        ResourceProjectionContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IResourceProjection>(
            new MacOSHostNetworkResource(resource));
}
