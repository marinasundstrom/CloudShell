namespace CloudShell.ResourceDefinitions;

public sealed record ResourceDefinitionGraphProjectionResult(
    IReadOnlyList<IResourceProjection> Resources,
    IReadOnlyList<ResourceDefinitionDiagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(diagnostic =>
        diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error);

    public TProjection? Find<TProjection>(string resourceId)
        where TProjection : class, IResourceProjection =>
        Resources
            .OfType<TProjection>()
            .FirstOrDefault(resource =>
                string.Equals(
                    resource.Resource.Resource.EffectiveResourceId,
                    resourceId,
                    StringComparison.OrdinalIgnoreCase));
}

public sealed class ResourceDefinitionGraphProjectionResolver(
    ResourceProjectionResolver projectionResolver)
{
    public async ValueTask<ResourceDefinitionGraphProjectionResult> ProjectAsync(
        ResourceDefinitionGraphValidationPipelineResult validationResult,
        ResourceProjectionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(validationResult);
        ArgumentNullException.ThrowIfNull(context);

        if (validationResult.HasErrors)
        {
            return new([], FlattenDiagnostics(validationResult));
        }

        var resources = new List<IResourceProjection>();
        var diagnostics = new List<ResourceDefinitionDiagnostic>();

        foreach (var resource in validationResult.Resources)
        {
            var projection = await projectionResolver.GetResourceProjectionAsync(
                resource.Projection,
                context,
                cancellationToken);

            if (projection is null)
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    ResourceDefinitionDiagnosticCodes.ResourceProjectionProviderMissing,
                    $"No resource projection provider is registered for resource type '{resource.Resource.Type.TypeId}'.",
                    resource.Resource.EffectiveResourceId));
                continue;
            }

            resources.Add(projection);
        }

        return new(resources, diagnostics);
    }

    private static IReadOnlyList<ResourceDefinitionDiagnostic> FlattenDiagnostics(
        ResourceDefinitionGraphValidationPipelineResult validationResult)
    {
        var diagnostics = new List<ResourceDefinitionDiagnostic>(validationResult.Diagnostics);

        foreach (var resource in validationResult.Resources)
        {
            diagnostics.AddRange(resource.Diagnostics);
        }

        return diagnostics;
    }
}
