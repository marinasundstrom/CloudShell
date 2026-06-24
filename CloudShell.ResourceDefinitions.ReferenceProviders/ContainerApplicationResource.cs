namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class ContainerApplicationResource(
    Resource resource) : IResourceProjection
{
    public Resource Resource { get; } = resource;

    public string? Image =>
        Resource.Attributes.GetString(
            ContainerApplicationResourceTypeProvider.Attributes.ContainerImage);

    public int Replicas =>
        int.TryParse(
            Resource.Attributes.GetString(
                ContainerApplicationResourceTypeProvider.Attributes.ContainerReplicas),
            out var replicas)
                ? replicas
                : 1;

    public ValueTask<IReadOnlyList<VolumeMountDefinition>> GetVolumesAsync(
        CancellationToken cancellationToken = default)
    {
        var volumeConsumer = Resource.Capabilities.Get<VolumeConsumerCapability>();

        return ValueTask.FromResult(volumeConsumer?.Mounts ?? []);
    }

    public ValueTask<ContainerApplicationLifecycleOperation?> GetStartOperationAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(
            Resource.Operations.Get(ContainerApplicationResourceTypeProvider.Operations.Start)
                as ContainerApplicationLifecycleOperation);

    public ValueTask<ContainerApplicationLifecycleOperation?> GetRestartOperationAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(
            Resource.Operations.Get(ContainerApplicationResourceTypeProvider.Operations.Restart)
                as ContainerApplicationLifecycleOperation);

    public ValueTask<ContainerApplicationImageUpdateOperation?> GetImageUpdateOperationAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(
            Resource.Operations.Get(ContainerApplicationResourceTypeProvider.Operations.UpdateImage)
                as ContainerApplicationImageUpdateOperation);
}

public sealed class ContainerApplicationResourceProjectionProvider : IResourceProjectionProvider
{
    public ResourceTypeId TypeId => ContainerApplicationResourceTypeProvider.ResourceTypeId;

    public bool CanProject(Resource resource) =>
        resource.Type.TypeId == ContainerApplicationResourceTypeProvider.ResourceTypeId;

    public ValueTask<IResourceProjection> ProjectAsync(
        Resource resource,
        ResourceProjectionContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IResourceProjection>(
            new ContainerApplicationResource(resource));
}
