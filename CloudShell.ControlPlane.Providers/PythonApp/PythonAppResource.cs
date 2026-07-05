namespace CloudShell.ControlPlane.Providers;

public sealed class PythonAppResource(
    Resource resource) : IResourceProjection
{
    public Resource Resource { get; } = resource;

    public string? ProjectPath =>
        Resource.Attributes.GetString(
            PythonAppResourceTypeProvider.Attributes.ProjectPath);

    public string? Command =>
        Resource.Attributes.GetString(
            PythonAppResourceTypeProvider.Attributes.Command);

    public string? ScriptPath =>
        Resource.Attributes.GetString(
            PythonAppResourceTypeProvider.Attributes.ScriptPath);

    public string? Module =>
        Resource.Attributes.GetString(
            PythonAppResourceTypeProvider.Attributes.Module);

    public string? Arguments =>
        Resource.Attributes.GetString(
            PythonAppResourceTypeProvider.Attributes.Arguments);

    public IReadOnlyList<NetworkingEndpointRequestValue> EndpointRequests =>
        Resource.Attributes.GetObject<NetworkingEndpointRequestValue[]>(
            PythonAppResourceTypeProvider.Attributes.EndpointRequests) ?? [];

    public string? ServiceDiscoveryName =>
        Resource.Attributes.GetString(
            PythonAppResourceTypeProvider.Attributes.ServiceDiscoveryName);

    public IReadOnlyList<ResourceReference> References =>
        Resource.Attributes.GetObject<ResourceReference[]>(
            PythonAppResourceTypeProvider.Attributes.References) ?? [];

    public ValueTask<IReadOnlyList<VolumeMountDefinition>> GetVolumesAsync(
        CancellationToken cancellationToken = default)
    {
        var volumeConsumer = Resource.Capabilities.Get<VolumeConsumerCapability>();

        return ValueTask.FromResult(volumeConsumer?.Mounts ?? []);
    }
}

public sealed class PythonAppResourceProjectionProvider : IResourceProjectionProvider
{
    public ResourceTypeId TypeId => PythonAppResourceTypeProvider.ResourceTypeId;

    public bool CanProject(Resource resource) =>
        resource.Type.TypeId == PythonAppResourceTypeProvider.ResourceTypeId;

    public ValueTask<IResourceProjection> ProjectAsync(
        Resource resource,
        ResourceProjectionContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IResourceProjection>(
            new PythonAppResource(resource));
}
