namespace CloudShell.ResourceDefinitions;

public interface IResourceCapabilityProjection
{
    ResourceCapabilityId CapabilityId { get; }
}

public interface IResourceCapabilityProjector
{
    ResourceCapabilityId CapabilityId { get; }

    bool CanProject(
        Resource resource,
        ResourceCapabilityResolution capability);

    ValueTask<IResourceCapabilityProjection> ProjectAsync(
        ResourceDefinitionProjection resource,
        ResourceCapabilityResolution capability,
        ResourceCapabilityProjectionContext context,
        CancellationToken cancellationToken = default);
}

public sealed record ResourceCapabilityProjectionContext(
    string? EnvironmentId = null,
    string? PrincipalId = null);

public sealed class ResourceCapabilityResolver(
    IEnumerable<IResourceCapabilityProjector> capabilityProjectors)
{
    private readonly IReadOnlyList<IResourceCapabilityProjector> _capabilityProjectors =
        capabilityProjectors.ToArray();

    public async ValueTask<IResourceCapabilityProjection?> ResolveAsync(
        ResourceDefinitionProjection resource,
        ResourceCapabilityId capabilityId,
        ResourceCapabilityProjectionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(context);

        var capability = resource.Resource.Capabilities.Resolve(capabilityId);
        if (capability is null)
        {
            return null;
        }

        var projector = _capabilityProjectors.FirstOrDefault(projector =>
            projector.CapabilityId == capability.Id &&
            projector.CanProject(resource.Resource, capability));

        if (projector is null)
        {
            return null;
        }

        return await projector.ProjectAsync(
            resource,
            capability,
            context,
            cancellationToken);
    }

    public async ValueTask<TCapability?> ResolveAsync<TCapability>(
        ResourceDefinitionProjection resource,
        ResourceCapabilityId capabilityId,
        ResourceCapabilityProjectionContext context,
        CancellationToken cancellationToken = default)
        where TCapability : class, IResourceCapabilityProjection =>
        await ResolveAsync(resource, capabilityId, context, cancellationToken) as TCapability;
}

public sealed class ResourceDefinitionProjection(
    Resource resource,
    ResourceCapabilityResolver capabilityResolver,
    ResourceCapabilityProjectionContext context)
{
    public Resource Resource { get; } = resource;

    public ResourceDefinition Definition => Resource.ToDefinition();

    public ResourceAttributeSet Attributes => Resource.Attributes;

    public ResourceCapabilitySet Capabilities => Resource.Capabilities;

    public ResourceOperationSet Operations => Resource.Operations;

    public ValueTask<TCapability?> GetCapabilityAsync<TCapability>(
        ResourceCapabilityId capabilityId,
        CancellationToken cancellationToken = default)
        where TCapability : class, IResourceCapabilityProjection =>
        capabilityResolver.ResolveAsync<TCapability>(
            this,
            capabilityId,
            context,
            cancellationToken);
}

public interface IResourceProjection
{
    ResourceDefinitionProjection Resource { get; }
}

public interface IResourceProjectionProvider
{
    ResourceTypeId TypeId { get; }

    bool CanProject(ResourceDefinitionProjection resource);

    ValueTask<IResourceProjection> ProjectAsync(
        ResourceDefinitionProjection resource,
        ResourceProjectionContext context,
        CancellationToken cancellationToken = default);
}

public sealed record ResourceProjectionContext(
    string? EnvironmentId = null,
    string? PrincipalId = null);

public sealed class ResourceProjectionResolver(
    IEnumerable<IResourceProjectionProvider> projectionProviders)
{
    private readonly IReadOnlyList<IResourceProjectionProvider> _projectionProviders =
        projectionProviders.ToArray();

    public async ValueTask<IResourceProjection?> GetResourceProjectionAsync(
        ResourceDefinitionProjection resource,
        ResourceProjectionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(context);

        var provider = _projectionProviders.FirstOrDefault(provider =>
            provider.TypeId == resource.Resource.Type.TypeId &&
            provider.CanProject(resource));

        if (provider is null)
        {
            return null;
        }

        return await provider.ProjectAsync(resource, context, cancellationToken);
    }

    public async ValueTask<TProjection?> GetResourceProjectionAsync<TProjection>(
        ResourceDefinitionProjection resource,
        ResourceProjectionContext context,
        CancellationToken cancellationToken = default)
        where TProjection : class, IResourceProjection =>
        await GetResourceProjectionAsync(resource, context, cancellationToken) as TProjection;
}
