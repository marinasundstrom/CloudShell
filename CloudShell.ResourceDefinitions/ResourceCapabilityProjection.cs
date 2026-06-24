namespace CloudShell.ResourceDefinitions;

public interface IResourceCapabilityProjection
{
    ResourceCapabilityId CapabilityId { get; }
}

public interface IResourceDefinitionCapabilityProjector
{
    ResourceCapabilityId CapabilityId { get; }

    bool CanProject(
        ResolvedResourceDefinition resource,
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
    IEnumerable<IResourceDefinitionCapabilityProjector> capabilityProjectors)
{
    private readonly IReadOnlyList<IResourceDefinitionCapabilityProjector> _capabilityProjectors =
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
    ResolvedResourceDefinition resource,
    ResourceCapabilityResolver capabilityResolver,
    ResourceCapabilityProjectionContext context)
{
    public ResolvedResourceDefinition Resource { get; } = resource;

    public ResourceDefinition Definition => Resource.Definition;

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
