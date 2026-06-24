namespace CloudShell.ResourceDefinitions;

public sealed record ResourceDeploymentDefinition(
    string Name,
    IReadOnlyList<ResourceDefinition> Resources,
    string? EnvironmentId = null,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public ResourceDefinitionGraph ToGraph() => new(Resources);
}

public sealed record ResourceDefinitionGraph(
    IReadOnlyList<ResourceDefinition> Resources);

public sealed class ResourceDefinitionGraphValidationPipeline(
    ResourceDefinitionValidationPipeline resourcePipeline)
{
    public ValueTask<ResourceDefinitionGraphValidationPipelineResult> ValidateAsync(
        ResourceDeploymentDefinition deployment,
        ResourceDefinitionValidationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentNullException.ThrowIfNull(context);

        var effectiveContext = string.IsNullOrWhiteSpace(deployment.EnvironmentId)
            ? context
            : context with { EnvironmentId = deployment.EnvironmentId };

        return ValidateAsync(
            deployment.ToGraph(),
            effectiveContext,
            cancellationToken);
    }

    public async ValueTask<ResourceDefinitionGraphValidationPipelineResult> ValidateAsync(
        ResourceDefinitionGraph graph,
        ResourceDefinitionValidationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(context);

        var diagnostics = new List<ResourceDefinitionDiagnostic>();
        var resourceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in graph.Resources)
        {
            if (!resourceIds.Add(definition.EffectiveResourceId))
            {
                duplicateIds.Add(definition.EffectiveResourceId);
            }
        }

        foreach (var duplicateId in duplicateIds)
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                ResourceDefinitionDiagnosticCodes.DuplicateResourceDefinition,
                $"Resource definition '{duplicateId}' is declared more than once in the graph.",
                duplicateId));
        }

        foreach (var definition in graph.Resources)
        {
            foreach (var dependency in definition.ResourceDependencies)
            {
                if (!resourceIds.Contains(dependency))
                {
                    diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                        ResourceDefinitionDiagnosticCodes.ResourceDependencyMissing,
                        $"Resource definition '{definition.EffectiveResourceId}' depends on missing resource '{dependency}'.",
                        definition.EffectiveResourceId));
                }
            }
        }

        var resources = new List<ResourceDefinitionValidationPipelineResult>();
        foreach (var definition in graph.Resources)
        {
            resources.Add(await resourcePipeline.ValidateAsync(
                definition,
                context,
                cancellationToken));
        }

        return new(graph, resources, diagnostics);
    }
}

public sealed record ResourceDefinitionGraphValidationPipelineResult(
    ResourceDefinitionGraph Graph,
    IReadOnlyList<ResourceDefinitionValidationPipelineResult> Resources,
    IReadOnlyList<ResourceDefinitionDiagnostic> Diagnostics)
{
    public bool HasErrors =>
        Diagnostics.Any(diagnostic => diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error) ||
        Resources.Any(resource => resource.HasErrors);

    public ResourceDefinitionValidationPipelineResult? FindResource(string resourceId) =>
        Resources.FirstOrDefault(resource =>
            string.Equals(
                resource.Resource.Definition.EffectiveResourceId,
                resourceId,
                StringComparison.OrdinalIgnoreCase));
}
