namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class ExecutableApplicationResource(
    ResourceDefinitionProjection resource) : IResourceProjection
{
    public ResourceDefinitionProjection Resource { get; } = resource;

    public string? ExecutablePath =>
        Resource.Attributes.GetString(
            ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath);

    public async ValueTask<IReadOnlyList<VolumeMountDefinition>> GetVolumesAsync(
        CancellationToken cancellationToken = default)
    {
        var volumeConsumer = await Resource.GetCapabilityAsync<VolumeConsumerCapability>(
            VolumeConsumerCapabilityProvider.CapabilityIdValue,
            cancellationToken);

        return volumeConsumer?.Mounts ?? [];
    }
}

public sealed class ExecutableApplicationResourceProjectionProvider : IResourceProjectionProvider
{
    public ResourceTypeId TypeId => ExecutableApplicationResourceTypeProvider.ResourceTypeId;

    public bool CanProject(ResourceDefinitionProjection resource) =>
        resource.Resource.TypeDefinition.TypeId == ExecutableApplicationResourceTypeProvider.ResourceTypeId;

    public ValueTask<IResourceProjection> ProjectAsync(
        ResourceDefinitionProjection resource,
        ResourceProjectionContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IResourceProjection>(
            new ExecutableApplicationResource(resource));
}
