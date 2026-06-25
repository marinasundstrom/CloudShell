namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class AspNetCoreProjectResource(
    Resource resource) : IResourceProjection
{
    public Resource Resource { get; } = resource;

    public string? ProjectPath =>
        Resource.Attributes.GetString(
            AspNetCoreProjectResourceTypeProvider.Attributes.ProjectPath);

    public string? Arguments =>
        Resource.Attributes.GetString(
            AspNetCoreProjectResourceTypeProvider.Attributes.ProjectArguments);

    public bool HotReload =>
        GetBoolean(
            AspNetCoreProjectResourceTypeProvider.Attributes.HotReload,
            defaultValue: true);

    public bool UseLaunchSettings =>
        GetBoolean(
            AspNetCoreProjectResourceTypeProvider.Attributes.UseLaunchSettings,
            defaultValue: true);

    public IReadOnlyList<NetworkingEndpointRequestValue> EndpointRequests =>
        Resource.Attributes.GetObject<NetworkingEndpointRequestValue[]>(
            AspNetCoreProjectResourceTypeProvider.Attributes.EndpointRequests) ?? [];

    public ValueTask<IReadOnlyList<VolumeMountDefinition>> GetVolumesAsync(
        CancellationToken cancellationToken = default)
    {
        var volumeConsumer = Resource.Capabilities.Get<VolumeConsumerCapability>();

        return ValueTask.FromResult(volumeConsumer?.Mounts ?? []);
    }

    public ValueTask<AspNetCoreProjectLifecycleOperation?> GetStartOperationAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(
            Resource.Operations.Get(AspNetCoreProjectResourceTypeProvider.Operations.Start)
                as AspNetCoreProjectLifecycleOperation);

    public ValueTask<AspNetCoreProjectLifecycleOperation?> GetStopOperationAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(
            Resource.Operations.Get(AspNetCoreProjectResourceTypeProvider.Operations.Stop)
                as AspNetCoreProjectLifecycleOperation);

    public ValueTask<AspNetCoreProjectLifecycleOperation?> GetRestartOperationAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(
            Resource.Operations.Get(AspNetCoreProjectResourceTypeProvider.Operations.Restart)
                as AspNetCoreProjectLifecycleOperation);

    private bool GetBoolean(
        ResourceAttributeId attributeId,
        bool defaultValue) =>
        bool.TryParse(Resource.Attributes.GetString(attributeId), out var value)
            ? value
            : defaultValue;
}

public sealed class AspNetCoreProjectResourceProjectionProvider : IResourceProjectionProvider
{
    public ResourceTypeId TypeId => AspNetCoreProjectResourceTypeProvider.ResourceTypeId;

    public bool CanProject(Resource resource) =>
        resource.Type.TypeId == AspNetCoreProjectResourceTypeProvider.ResourceTypeId;

    public ValueTask<IResourceProjection> ProjectAsync(
        Resource resource,
        ResourceProjectionContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IResourceProjection>(
            new AspNetCoreProjectResource(resource));
}
