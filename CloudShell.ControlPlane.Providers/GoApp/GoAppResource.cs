namespace CloudShell.ControlPlane.Providers;

public sealed class GoAppResource(
    Resource resource) : IResourceProjection
{
    public Resource Resource { get; } = resource;

    public string? ProjectPath =>
        Resource.Attributes.GetString(
            GoAppResourceTypeProvider.Attributes.ProjectPath);

    public string? Command =>
        Resource.Attributes.GetString(
            GoAppResourceTypeProvider.Attributes.Command);

    public string? PackagePath =>
        Resource.Attributes.GetString(
            GoAppResourceTypeProvider.Attributes.PackagePath);

    public string? BinaryPath =>
        Resource.Attributes.GetString(
            GoAppResourceTypeProvider.Attributes.BinaryPath);

    public string? Arguments =>
        Resource.Attributes.GetString(
            GoAppResourceTypeProvider.Attributes.Arguments);

    public IReadOnlyList<NetworkingEndpointRequestValue> EndpointRequests =>
        Resource.Attributes.GetObject<NetworkingEndpointRequestValue[]>(
            GoAppResourceTypeProvider.Attributes.EndpointRequests) ?? [];

    public string? ServiceDiscoveryName =>
        Resource.Attributes.GetString(
            GoAppResourceTypeProvider.Attributes.ServiceDiscoveryName);

    public IReadOnlyList<ResourceReference> References =>
        Resource.Attributes.GetObject<ResourceReference[]>(
            GoAppResourceTypeProvider.Attributes.References) ?? [];

    public ValueTask<IReadOnlyList<VolumeMountDefinition>> GetVolumesAsync(
        CancellationToken cancellationToken = default)
    {
        var volumeConsumer = Resource.Capabilities.Get<VolumeConsumerCapability>();

        return ValueTask.FromResult(volumeConsumer?.Mounts ?? []);
    }
}

public sealed class GoAppResourceProjectionProvider : IResourceProjectionProvider
{
    public ResourceTypeId TypeId => GoAppResourceTypeProvider.ResourceTypeId;

    public bool CanProject(Resource resource) =>
        resource.Type.TypeId == GoAppResourceTypeProvider.ResourceTypeId;

    public ValueTask<IResourceProjection> ProjectAsync(
        Resource resource,
        ResourceProjectionContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IResourceProjection>(
            new GoAppResource(resource));
}
