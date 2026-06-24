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
        Resource resource,
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
        Resource resource,
        ResourceCapabilityId capabilityId,
        ResourceCapabilityProjectionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(context);

        var capability = resource.Capabilities.Resolve(capabilityId);
        if (capability is null)
        {
            return null;
        }

        var projector = _capabilityProjectors.FirstOrDefault(projector =>
            projector.CapabilityId == capability.Id &&
            projector.CanProject(resource, capability));

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
        Resource resource,
        ResourceCapabilityId capabilityId,
        ResourceCapabilityProjectionContext context,
        CancellationToken cancellationToken = default)
        where TCapability : class, IResourceCapabilityProjection =>
        await ResolveAsync(resource, capabilityId, context, cancellationToken) as TCapability;
}

public interface IResourceProjection
{
    Resource Resource { get; }
}

public interface IResourceProjectionProvider
{
    ResourceTypeId TypeId { get; }

    bool CanProject(Resource resource);

    ValueTask<IResourceProjection> ProjectAsync(
        Resource resource,
        ResourceProjectionContext context,
        CancellationToken cancellationToken = default);
}

public sealed record ResourceProjectionContext(
    string? EnvironmentId = null,
    string? PrincipalId = null,
    ResourceCapabilityResolver? CapabilityResolver = null);

public sealed class ResourceProjectionResolver(
    IEnumerable<IResourceProjectionProvider> projectionProviders,
    ResourceCapabilityResolver? capabilityResolver = null)
{
    private readonly IReadOnlyList<IResourceProjectionProvider> _projectionProviders =
        projectionProviders.ToArray();

    public async ValueTask<IResourceProjection?> GetResourceProjectionAsync(
        Resource resource,
        ResourceProjectionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(context);

        var provider = _projectionProviders.FirstOrDefault(provider =>
            provider.TypeId == resource.Type.TypeId &&
            provider.CanProject(resource));

        if (provider is null)
        {
            return null;
        }

        var resolvedContext = context.CapabilityResolver is null && capabilityResolver is not null
            ? context with { CapabilityResolver = capabilityResolver }
            : context;

        return await provider.ProjectAsync(resource, resolvedContext, cancellationToken);
    }

    public async ValueTask<TProjection?> GetResourceProjectionAsync<TProjection>(
        Resource resource,
        ResourceProjectionContext context,
        CancellationToken cancellationToken = default)
        where TProjection : class, IResourceProjection =>
        await GetResourceProjectionAsync(resource, context, cancellationToken) as TProjection;
}
