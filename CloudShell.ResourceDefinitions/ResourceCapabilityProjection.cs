namespace CloudShell.ResourceDefinitions;

public interface IResourceCapabilityProjection
{
    Resource Resource { get; }

    ResourceProjectionExecutionContext Context { get; }

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
    string? PrincipalId = null,
    ResourceProjectionExecutionContext? ExecutionContext = null)
{
    public ResourceCapabilityProjectionContext ForResource(Resource resource) =>
        this with
        {
            ExecutionContext = ExecutionContext?.ForResource(resource) ??
                new ResourceProjectionExecutionContext(resource)
        };
}

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
            context.ForResource(resource),
            cancellationToken);
    }

    public async ValueTask<TCapability?> ResolveAsync<TCapability>(
        Resource resource,
        ResourceCapabilityId capabilityId,
        ResourceCapabilityProjectionContext context,
        CancellationToken cancellationToken = default)
        where TCapability : class, IResourceCapabilityProjection =>
        await ResolveAsync(resource, capabilityId, context, cancellationToken) as TCapability;

    public async ValueTask<Resource> BindAsync(
        Resource resource,
        ResourceCapabilityProjectionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(context);

        var projections = new List<IResourceCapabilityProjection>();

        foreach (var capability in resource.Capabilities)
        {
            var projection = await ResolveAsync(
                resource,
                capability.Id,
                context,
                cancellationToken);

            if (projection is not null)
            {
                projections.Add(projection);
            }
        }

        resource.Capabilities.SetProjections(projections);
        return resource;
    }
}

public interface IResourceOperationProjection
{
    Resource Resource { get; }

    ResourceProjectionExecutionContext Context { get; }

    ResourceOperationResolution Definition { get; }

    ResourceOperationId OperationId { get; }
}

public sealed record ResourceOperationExecutionResult(
    Resource Resource,
    ResourceOperationId OperationId,
    IReadOnlyList<ResourceDefinitionDiagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(diagnostic =>
        diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error);
}

public interface IResourceOperationProjector
{
    ResourceOperationId OperationId { get; }

    ResourceDefinitionValueSource ResolutionLevel { get; }

    bool CanProject(
        Resource resource,
        ResourceOperationResolution operation);

    ValueTask<IResourceOperationProjection> ProjectAsync(
        Resource resource,
        ResourceOperationResolution operation,
        ResourceOperationProjectionContext context,
        CancellationToken cancellationToken = default);
}

public sealed record ResourceOperationProjectionContext(
    string? EnvironmentId = null,
    string? PrincipalId = null,
    ResourceProjectionExecutionContext? ExecutionContext = null)
{
    public ResourceOperationProjectionContext ForResource(Resource resource) =>
        this with
        {
            ExecutionContext = ExecutionContext?.ForResource(resource) ??
                new ResourceProjectionExecutionContext(resource)
        };
}

public sealed class ResourceOperationResolver(
    IEnumerable<IResourceOperationProjector> operationProjectors)
{
    private readonly IReadOnlyList<IResourceOperationProjector> _operationProjectors =
        operationProjectors.ToArray();

    public async ValueTask<IResourceOperationProjection?> ResolveAsync(
        Resource resource,
        ResourceOperationId operationId,
        ResourceOperationProjectionContext context,
        ResourceDefinitionValueSource? source = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(context);

        if (!resource.Operations.Has(operationId))
        {
            return null;
        }

        var operation = resource.Operations.Resolve(operationId, source);
        var projector = _operationProjectors.FirstOrDefault(projector =>
            projector.OperationId == operation.Id &&
            projector.ResolutionLevel == operation.Source &&
            projector.CanProject(resource, operation));

        if (projector is null)
        {
            return null;
        }

        return await projector.ProjectAsync(
            resource,
            operation,
            context.ForResource(resource),
            cancellationToken);
    }

    public async ValueTask<TOperation?> ResolveAsync<TOperation>(
        Resource resource,
        ResourceOperationId operationId,
        ResourceOperationProjectionContext context,
        ResourceDefinitionValueSource? source = null,
        CancellationToken cancellationToken = default)
        where TOperation : class, IResourceOperationProjection =>
        await ResolveAsync(
            resource,
            operationId,
            context,
            source,
            cancellationToken) as TOperation;

    public async ValueTask<Resource> BindAsync(
        Resource resource,
        ResourceOperationProjectionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(context);

        var projections = new List<IResourceOperationProjection>();

        foreach (var operation in resource.Operations)
        {
            var projection = await ResolveAsync(
                resource,
                operation.Id,
                context,
                operation.Source,
                cancellationToken);

            if (projection is not null)
            {
                projections.Add(projection);
            }
        }

        resource.Operations.SetProjections(projections);
        return resource;
    }
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
    ResourceCapabilityResolver? CapabilityResolver = null,
    ResourceOperationResolver? OperationResolver = null);

public sealed class ResourceProjectionExecutionContext(Resource resource)
{
    public Resource Resource { get; } = resource;

    public ResourceProjectionExecutionContext ForResource(Resource targetResource) =>
        ReferenceEquals(Resource, targetResource)
            ? this
            : new ResourceProjectionExecutionContext(targetResource);

    public ResourceChangeContext CreateChangeContext() =>
        Resource.CreateChangeContext();
}

public sealed class ResourceProjectionResolver(
    IEnumerable<IResourceProjectionProvider> projectionProviders,
    ResourceCapabilityResolver? capabilityResolver = null,
    ResourceOperationResolver? operationResolver = null)
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

        var resolvedContext = context with
        {
            CapabilityResolver = context.CapabilityResolver ?? capabilityResolver,
            OperationResolver = context.OperationResolver ?? operationResolver
        };

        if (resolvedContext.CapabilityResolver is not null)
        {
            await resolvedContext.CapabilityResolver.BindAsync(
                resource,
                new ResourceCapabilityProjectionContext(
                    resolvedContext.EnvironmentId,
                    resolvedContext.PrincipalId,
                    new ResourceProjectionExecutionContext(resource)),
                cancellationToken);
        }

        if (resolvedContext.OperationResolver is not null)
        {
            await resolvedContext.OperationResolver.BindAsync(
                resource,
                new ResourceOperationProjectionContext(
                    resolvedContext.EnvironmentId,
                    resolvedContext.PrincipalId,
                    new ResourceProjectionExecutionContext(resource)),
                cancellationToken);
        }

        return await provider.ProjectAsync(resource, resolvedContext, cancellationToken);
    }

    public async ValueTask<TProjection?> GetResourceProjectionAsync<TProjection>(
        Resource resource,
        ResourceProjectionContext context,
        CancellationToken cancellationToken = default)
        where TProjection : class, IResourceProjection =>
        await GetResourceProjectionAsync(resource, context, cancellationToken) as TProjection;
}
