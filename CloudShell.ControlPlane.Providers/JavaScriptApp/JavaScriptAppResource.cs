namespace CloudShell.ControlPlane.Providers;

public sealed class JavaScriptAppResource(
    Resource resource) : IResourceProjection
{
    public Resource Resource { get; } = resource;

    public string? ProjectPath =>
        Resource.Attributes.GetString(
            JavaScriptAppResourceTypeProvider.Attributes.ProjectPath);

    public string? Engine =>
        Resource.Attributes.GetString(
            JavaScriptAppResourceTypeProvider.Attributes.Engine);

    public string? PackageManager =>
        Resource.Attributes.GetString(
            JavaScriptAppResourceTypeProvider.Attributes.PackageManager);

    public string? Script =>
        Resource.Attributes.GetString(
            JavaScriptAppResourceTypeProvider.Attributes.Script);

    public string? Arguments =>
        Resource.Attributes.GetString(
            JavaScriptAppResourceTypeProvider.Attributes.Arguments);

    public IReadOnlyList<NetworkingEndpointRequestValue> EndpointRequests =>
        Resource.Attributes.GetObject<NetworkingEndpointRequestValue[]>(
            JavaScriptAppResourceTypeProvider.Attributes.EndpointRequests) ?? [];

    public string? ServiceDiscoveryName =>
        Resource.Attributes.GetString(
            JavaScriptAppResourceTypeProvider.Attributes.ServiceDiscoveryName);

    public IReadOnlyList<ResourceReference> References =>
        Resource.Attributes.GetObject<ResourceReference[]>(
            JavaScriptAppResourceTypeProvider.Attributes.References) ?? [];

    public ValueTask<IReadOnlyList<VolumeMountDefinition>> GetVolumesAsync(
        CancellationToken cancellationToken = default)
    {
        var volumeConsumer = Resource.Capabilities.Get<VolumeConsumerCapability>();

        return ValueTask.FromResult(volumeConsumer?.Mounts ?? []);
    }
}

public sealed class JavaScriptAppResourceProjectionProvider : IResourceProjectionProvider
{
    public ResourceTypeId TypeId => JavaScriptAppResourceTypeProvider.ResourceTypeId;

    public bool CanProject(Resource resource) =>
        resource.Type.TypeId == JavaScriptAppResourceTypeProvider.ResourceTypeId;

    public ValueTask<IResourceProjection> ProjectAsync(
        Resource resource,
        ResourceProjectionContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IResourceProjection>(
            new JavaScriptAppResource(resource));
}
