namespace CloudShell.ResourceModel.ReferenceProviders;

public sealed class ExecutableApplicationResource(
    Resource resource) : IResourceProjection
{
    public Resource Resource { get; } = resource;

    public string? ExecutablePath =>
        Resource.Attributes.GetString(
            ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath);

    public ValueTask<IReadOnlyList<VolumeMountDefinition>> GetVolumesAsync(
        CancellationToken cancellationToken = default)
    {
        var volumeConsumer = Resource.Capabilities.Get<VolumeConsumerCapability>();

        return ValueTask.FromResult(volumeConsumer?.Mounts ?? []);
    }

    public ValueTask<ExecutableStartOperation?> GetStartOperationAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Resource.Operations.Get<ExecutableStartOperation>());
}

public sealed class ExecutableApplicationResourceProjectionProvider : IResourceProjectionProvider
{
    public ResourceTypeId TypeId => ExecutableApplicationResourceTypeProvider.ResourceTypeId;

    public bool CanProject(Resource resource) =>
        resource.Type.TypeId == ExecutableApplicationResourceTypeProvider.ResourceTypeId;

    public ValueTask<IResourceProjection> ProjectAsync(
        Resource resource,
        ResourceProjectionContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IResourceProjection>(
            new ExecutableApplicationResource(resource));
}
