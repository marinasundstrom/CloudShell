namespace CloudShell.ControlPlane.Providers;

public sealed class JavaAppResource(
    Resource resource) : IResourceProjection
{
    public Resource Resource { get; } = resource;

    public string? ProjectPath =>
        Resource.Attributes.GetString(
            JavaAppResourceTypeProvider.Attributes.ProjectPath);

    public string? Command =>
        Resource.Attributes.GetString(
            JavaAppResourceTypeProvider.Attributes.Command);

    public string? BuildTool =>
        Resource.Attributes.GetString(
            JavaAppResourceTypeProvider.Attributes.BuildTool);

    public string? BuildArguments =>
        Resource.Attributes.GetString(
            JavaAppResourceTypeProvider.Attributes.BuildArguments);

    public string? ArtifactPath =>
        Resource.Attributes.GetString(
            JavaAppResourceTypeProvider.Attributes.ArtifactPath);

    public string? MainClass =>
        Resource.Attributes.GetString(
            JavaAppResourceTypeProvider.Attributes.MainClass);

    public string? ClassPath =>
        Resource.Attributes.GetString(
            JavaAppResourceTypeProvider.Attributes.ClassPath);

    public string? JvmArguments =>
        Resource.Attributes.GetString(
            JavaAppResourceTypeProvider.Attributes.JvmArguments);

    public string? Arguments =>
        Resource.Attributes.GetString(
            JavaAppResourceTypeProvider.Attributes.Arguments);

    public IReadOnlyList<NetworkingEndpointRequestValue> EndpointRequests =>
        Resource.Attributes.GetObject<NetworkingEndpointRequestValue[]>(
            JavaAppResourceTypeProvider.Attributes.EndpointRequests) ?? [];

    public string? ServiceDiscoveryName =>
        Resource.Attributes.GetString(
            JavaAppResourceTypeProvider.Attributes.ServiceDiscoveryName);

    public IReadOnlyList<ResourceReference> References =>
        Resource.Attributes.GetObject<ResourceReference[]>(
            JavaAppResourceTypeProvider.Attributes.References) ?? [];

    public ValueTask<IReadOnlyList<VolumeMountDefinition>> GetVolumesAsync(
        CancellationToken cancellationToken = default)
    {
        var volumeConsumer = Resource.Capabilities.Get<VolumeConsumerCapability>();

        return ValueTask.FromResult(volumeConsumer?.Mounts ?? []);
    }
}

public sealed class JavaAppResourceProjectionProvider : IResourceProjectionProvider
{
    public ResourceTypeId TypeId => JavaAppResourceTypeProvider.ResourceTypeId;

    public bool CanProject(Resource resource) =>
        resource.Type.TypeId == JavaAppResourceTypeProvider.ResourceTypeId;

    public ValueTask<IResourceProjection> ProjectAsync(
        Resource resource,
        ResourceProjectionContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IResourceProjection>(
            new JavaAppResource(resource));
}
