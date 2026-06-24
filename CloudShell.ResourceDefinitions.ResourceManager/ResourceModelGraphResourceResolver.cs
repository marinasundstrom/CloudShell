namespace CloudShell.ResourceDefinitions.ResourceManager;

public sealed class ResourceModelGraphResourceResolver(
    ResourceGraphModel graphModel,
    ResourceResolver resourceResolver,
    ResourceGraphResolver graphResolver,
    ResourceCapabilityResolver capabilityResolver,
    ResourceOperationResolver operationResolver)
{
    private readonly ResourceGraphModel _graphModel =
        graphModel ?? throw new ArgumentNullException(nameof(graphModel));
    private readonly ResourceResolver _resourceResolver =
        resourceResolver ?? throw new ArgumentNullException(nameof(resourceResolver));
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
            : ResolveSingle(snapshot, resourceId, resolvedContext);

        foreach (var resource in resolution.Resources)
        {
            await BindProjectionsAsync(
                resource,
                resolvedContext,
                cancellationToken);
        }

        return new(
            snapshot,
            resolution.Resources,
            resolution.Diagnostics);
    }

    private ResourceGraphResolutionResult ResolveSingle(
        ResourceGraphSnapshot snapshot,
        string resourceId,
        ResourceDefinitionResolutionContext context)
    {
        var state = snapshot.Resources.FirstOrDefault(resource =>
            string.Equals(
                resource.EffectiveResourceId,
                resourceId.Trim(),
                StringComparison.OrdinalIgnoreCase));

        if (state is null)
        {
            return new(
                [],
                [
                    ResourceDefinitionDiagnostic.Error(
                        ResourceDefinitionDiagnosticCodes.ResourceGraphResourceMissing,
                        $"Resource graph state '{resourceId}' was not found.",
                        resourceId)
                ]);
        }

        var resource = _resourceResolver.Resolve(state, context);
        return new(
            [resource],
            resource.Diagnostics);
    }

    private async ValueTask BindProjectionsAsync(
        Resource resource,
        ResourceDefinitionResolutionContext context,
        CancellationToken cancellationToken)
    {
        await _capabilityResolver.BindAsync(
            resource,
            new ResourceCapabilityProjectionContext(
                context.EnvironmentId,
                context.PrincipalId,
                new ResourceProjectionExecutionContext(resource)),
            cancellationToken);

        await _operationResolver.BindAsync(
            resource,
            new ResourceOperationProjectionContext(
                context.EnvironmentId,
                context.PrincipalId,
                new ResourceProjectionExecutionContext(resource)),
            cancellationToken);
    }
}

public sealed record ResourceModelGraphResourceResolution(
    ResourceGraphSnapshot Snapshot,
    IReadOnlyList<Resource> Resources,
    IReadOnlyList<ResourceDefinitionDiagnostic> Diagnostics)
{
    public ResourceGraphVersion Version => Snapshot.Version;

    public Resource? Target => Resources.FirstOrDefault();

    public bool HasErrors => Diagnostics.Any(diagnostic =>
        diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error);
}
