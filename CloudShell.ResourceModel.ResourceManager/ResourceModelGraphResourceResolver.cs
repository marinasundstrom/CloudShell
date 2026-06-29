using ResourceManagerAction = CloudShell.Abstractions.ResourceManager.ResourceAction;

namespace CloudShell.ResourceModel.ResourceManager;

public sealed class ResourceModelGraphResourceResolver(
    ResourceGraphModel graphModel,
    ResourceGraphResolver graphResolver,
    ResourceCapabilityResolver capabilityResolver,
    ResourceOperationResolver operationResolver)
{
    private readonly ResourceGraphModel _graphModel =
        graphModel ?? throw new ArgumentNullException(nameof(graphModel));
    private readonly ResourceGraphResolver _graphResolver =
        graphResolver ?? throw new ArgumentNullException(nameof(graphResolver));
    private readonly ResourceCapabilityResolver _capabilityResolver =
        capabilityResolver ?? throw new ArgumentNullException(nameof(capabilityResolver));
    private readonly ResourceOperationResolver _operationResolver =
        operationResolver ?? throw new ArgumentNullException(nameof(operationResolver));

    public async ValueTask<ResourceModelGraphResourceResolution> ResolveAsync(
        string resourceId,
        ResourceDefinitionResolutionContext? context = null,
        CancellationToken cancellationToken = default) =>
        await ResolveCoreAsync(
            resourceId,
            includeDependencies: false,
            context,
            cancellationToken);

    public async ValueTask<ResourceModelGraphResourceResolution> ResolveWithDependenciesAsync(
        string resourceId,
        ResourceDefinitionResolutionContext? context = null,
        CancellationToken cancellationToken = default) =>
        await ResolveCoreAsync(
            resourceId,
            includeDependencies: true,
            context,
            cancellationToken);

    public async ValueTask<ResourceModelGraphReferenceResolution> ResolveReferenceAsync(
        ResourceReference reference,
        ResourceDefinitionResolutionContext? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);

        var snapshot = await _graphModel.GetSnapshotAsync(cancellationToken);
        var resolvedContext = context ?? ResourceDefinitionResolutionContext.Empty;
        var resolution = _graphResolver.ResolveReference(
            snapshot,
            reference,
            resolvedContext);

        if (resolution.IsResolved)
        {
            await BindProjectionsAsync(
                resolution.Resource!,
                [resolution.Resource!],
                resolvedContext,
                cancellationToken);
        }

        return new(
            snapshot,
            resolution.Reference,
            resolution.Resource,
            resolution.Diagnostics);
    }

    public async ValueTask<ResourceModelGraphCapabilityResolution> ResolveCapabilityAsync(
        string resourceId,
        ResourceCapabilityId capabilityId,
        ResourceDefinitionResolutionContext? context = null,
        CancellationToken cancellationToken = default)
    {
        var resourceResolution = await ResolveWithDependenciesAsync(
            resourceId,
            context,
            cancellationToken);
        var diagnostics = new List<ResourceDefinitionDiagnostic>(
            resourceResolution.Diagnostics);
        var resource = resourceResolution.Target;
        var capability = resource?.Capabilities.GetProjection(capabilityId);

        if (resource is not null && capability is null)
        {
            var capabilityResolution = resource.Capabilities.Resolve(capabilityId);
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                ResourceDefinitionDiagnosticCodes.ResourceCapabilityProjectionMissing,
                capabilityResolution is null
                    ? $"Capability '{capabilityId}' is not available."
                    : $"Capability '{capabilityId}' is declared but no capability projection is available.",
                resource.EffectiveResourceId));
        }

        return new(
            resourceResolution,
            capabilityId,
            capability,
            diagnostics);
    }

    public async ValueTask<ResourceModelGraphOperationResolution> ResolveOperationAsync(
        string resourceId,
        ResourceManagerAction action,
        ResourceDefinitionResolutionContext? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        return await ResolveOperationAsync(
            resourceId,
            ResourceOperationId.Create(action.Id),
            context,
            cancellationToken);
    }

    public async ValueTask<ResourceModelGraphOperationResolution> ResolveOperationAsync(
        string resourceId,
        ResourceOperationId operationId,
        ResourceDefinitionResolutionContext? context = null,
        CancellationToken cancellationToken = default)
    {
        var resourceResolution = await ResolveWithDependenciesAsync(
            resourceId,
            context,
            cancellationToken);
        var diagnostics = new List<ResourceDefinitionDiagnostic>(
            resourceResolution.Diagnostics);
        var resource = resourceResolution.Target;
        var operation = resource?.Operations.Get(operationId);

        if (resource is not null && operation is null)
        {
            var operationResolution = resource.Operations.Resolve(operationId);
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                ResourceDefinitionDiagnosticCodes.ResourceOperationProjectionMissing,
                operationResolution.IsAvailable
                    ? $"Operation '{operationId}' is declared but no operation projection is available."
                    : operationResolution.UnavailableReason ?? $"Operation '{operationId}' is not available.",
                resource.EffectiveResourceId));
        }

        return new(
            resourceResolution,
            operationId,
            operation,
            diagnostics);
    }

    private async ValueTask<ResourceModelGraphResourceResolution> ResolveCoreAsync(
        string resourceId,
        bool includeDependencies,
        ResourceDefinitionResolutionContext? context,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        var snapshot = await _graphModel.GetSnapshotAsync(cancellationToken);
        var resolvedContext = context ?? ResourceDefinitionResolutionContext.Empty;
        var resolution = includeDependencies
            ? _graphResolver.ResolveResourceAndDependencies(
                snapshot,
                resourceId,
                resolvedContext)
            : ToGraphResolutionResult(_graphResolver.ResolveResource(
                snapshot,
                resourceId,
                resolvedContext));

        foreach (var resource in resolution.Resources)
        {
            await BindProjectionsAsync(
                resource,
                resolution.Resources,
                resolvedContext,
                cancellationToken);
        }

        return new(
            snapshot,
            resolution.Resources,
            resolution.Diagnostics,
            resolution.ResolvedReferences);
    }

    private static ResourceGraphResolutionResult ToGraphResolutionResult(
        ResourceGraphResourceResolution resolution) =>
        new(
            resolution.Resource is null ? [] : [resolution.Resource],
            resolution.Diagnostics);

    private async ValueTask BindProjectionsAsync(
        Resource resource,
        IReadOnlyList<Resource> resources,
        ResourceDefinitionResolutionContext context,
        CancellationToken cancellationToken)
    {
        await _capabilityResolver.BindAsync(
            resource,
            new ResourceCapabilityProjectionContext(
                context.EnvironmentId,
                context.PrincipalId,
                new ResourceProjectionExecutionContext(resource, resources)),
            cancellationToken);

        await _operationResolver.BindAsync(
            resource,
            new ResourceOperationProjectionContext(
                context.EnvironmentId,
                context.PrincipalId,
                new ResourceProjectionExecutionContext(resource, resources)),
            cancellationToken);
    }
}

public sealed record ResourceModelGraphResourceResolution(
    ResourceGraphSnapshot Snapshot,
    IReadOnlyList<Resource> Resources,
    IReadOnlyList<ResourceDefinitionDiagnostic> Diagnostics,
    IReadOnlyList<ResourceGraphReferenceResolution>? ReferenceResolutions = null)
{
    public IReadOnlyList<ResourceGraphReferenceResolution> ResolvedReferences =>
        ReferenceResolutions ?? [];

    public ResourceGraphVersion Version => Snapshot.Version;

    public Resource? Target => Resources.FirstOrDefault();

    public bool HasErrors => Diagnostics.Any(diagnostic =>
        diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error);
}

public sealed record ResourceModelGraphCapabilityResolution(
    ResourceModelGraphResourceResolution ResourceResolution,
    ResourceCapabilityId CapabilityId,
    IResourceCapabilityProjection? Capability,
    IReadOnlyList<ResourceDefinitionDiagnostic> Diagnostics)
{
    public ResourceGraphSnapshot Snapshot => ResourceResolution.Snapshot;

    public ResourceGraphVersion Version => ResourceResolution.Version;

    public Resource? Resource => ResourceResolution.Target;

    public bool IsResolved => Capability is not null && !HasErrors;

    public bool HasErrors => Diagnostics.Any(diagnostic =>
        diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error);
}

public sealed record ResourceModelGraphReferenceResolution(
    ResourceGraphSnapshot Snapshot,
    ResourceReference Reference,
    Resource? Resource,
    IReadOnlyList<ResourceDefinitionDiagnostic> Diagnostics)
{
    public ResourceGraphVersion Version => Snapshot.Version;

    public bool IsResolved => Resource is not null && !HasErrors;

    public bool HasErrors => Diagnostics.Any(diagnostic =>
        diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error);
}

public sealed record ResourceModelGraphOperationResolution(
    ResourceModelGraphResourceResolution ResourceResolution,
    ResourceOperationId OperationId,
    IResourceOperationProjection? Operation,
    IReadOnlyList<ResourceDefinitionDiagnostic> Diagnostics)
{
    public ResourceGraphSnapshot Snapshot => ResourceResolution.Snapshot;

    public ResourceGraphVersion Version => ResourceResolution.Version;

    public Resource? Resource => ResourceResolution.Target;

    public bool IsResolved => Operation is not null && !HasErrors;

    public bool HasErrors => Diagnostics.Any(diagnostic =>
        diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error);
}
