namespace CloudShell.ResourceModel;

public sealed record ResourceTemplate(
    string Name,
    IReadOnlyList<ResourceDefinition> Resources,
    string? EnvironmentId = null,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public const string CurrentTemplateVersion = "1";

    public ResourceDefinitionGraph ToGraph() => new(Resources);
}

public sealed record ResourceDefinitionGraph(
    IReadOnlyList<ResourceDefinition> Resources);

public interface IResourceDefinitionGraphValidator
{
    ValueTask<ResourceDefinitionValidationResult> ValidateAsync(
        ResourceDefinitionGraphValidationContext context,
        CancellationToken cancellationToken = default);
}

public sealed record ResourceDefinitionGraphValidationContext(
    ResourceDefinitionGraph Graph,
    IReadOnlyList<Resource> Resources,
    string? EnvironmentId = null,
    string? PrincipalId = null)
{
    public Resource? FindResource(string resourceId) =>
        Resources.FirstOrDefault(resource =>
            string.Equals(
                resource.EffectiveResourceId,
                resourceId,
                StringComparison.OrdinalIgnoreCase));
}

public sealed class ResourceDefinitionGraphValidationPipeline
{
    private readonly ResourceDefinitionValidationPipeline _resourcePipeline;
    private readonly IReadOnlyList<IResourceDefinitionGraphValidator> _graphValidators;

    public ResourceDefinitionGraphValidationPipeline(
        ResourceDefinitionValidationPipeline resourcePipeline)
        : this(resourcePipeline, [])
    {
    }

    public ResourceDefinitionGraphValidationPipeline(
        ResourceDefinitionValidationPipeline resourcePipeline,
        IEnumerable<IResourceDefinitionGraphValidator> graphValidators)
    {
        _resourcePipeline = resourcePipeline ??
            throw new ArgumentNullException(nameof(resourcePipeline));
        _graphValidators = graphValidators?.ToArray() ??
            throw new ArgumentNullException(nameof(graphValidators));
    }

    public ValueTask<ResourceDefinitionGraphValidationPipelineResult> ValidateAsync(
        ResourceTemplate template,
        ResourceDefinitionValidationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(context);

        var effectiveContext = string.IsNullOrWhiteSpace(template.EnvironmentId)
            ? context
            : context with { EnvironmentId = template.EnvironmentId };

        return ValidateAsync(
            template.ToGraph(),
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
            foreach (var dependency in definition.StartupDependencyIds)
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
            resources.Add(await _resourcePipeline.ValidateAsync(
                definition,
                context,
                cancellationToken));
        }

        if (_graphValidators.Count > 0)
        {
            var graphContext = new ResourceDefinitionGraphValidationContext(
                graph,
                resources.Select(resource => resource.Resource).ToArray(),
                context.EnvironmentId,
                context.PrincipalId);

            foreach (var validator in _graphValidators)
            {
                var result = await validator.ValidateAsync(
                    graphContext,
                    cancellationToken);
                diagnostics.AddRange(result.Diagnostics);
            }
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
                resource.Resource.EffectiveResourceId,
                resourceId,
                StringComparison.OrdinalIgnoreCase));
}
