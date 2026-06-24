namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class ExecutableApplicationResource(
    Resource resource,
    ResourceCapabilityResolver capabilityResolver,
    ResourceCapabilityProjectionContext capabilityContext) : IResourceProjection
{
    public Resource Resource { get; } = resource;

    public string? ExecutablePath =>
        Resource.Attributes.GetString(
            ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath);

    public async ValueTask<IReadOnlyList<VolumeMountDefinition>> GetVolumesAsync(
        CancellationToken cancellationToken = default)
    {
        var volumeConsumer = await capabilityResolver.ResolveAsync<VolumeConsumerCapability>(
            Resource,
            VolumeConsumerCapabilityProvider.CapabilityIdValue,
            capabilityContext,
            cancellationToken);

        return volumeConsumer?.Mounts ?? [];
    }
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
            new ExecutableApplicationResource(
                resource,
                context.CapabilityResolver ?? new ResourceCapabilityResolver([]),
                new ResourceCapabilityProjectionContext(
                    context.EnvironmentId,
                    context.PrincipalId)));
}
