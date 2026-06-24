namespace CloudShell.ResourceDefinitions;

public interface IResourceDefinitionApplyProvider
{
    ResourceTypeId TypeId { get; }

    bool CanPlan(ResourceDefinitionProjection resource);

    ValueTask<ResourceDefinitionApplyPlan> PlanApplyAsync(
        ResourceDefinitionProjection resource,
        ResourceDefinitionApplyContext context,
        CancellationToken cancellationToken = default);
}

public sealed record ResourceDefinitionApplyContext(
    string? EnvironmentId = null,
    string? PrincipalId = null,
    bool Preview = true);

public enum ResourceDefinitionApplyStepKind
{
    AcceptDefinition,
    MaterializeRuntime,
    UpdateDefinition
}

public sealed record ResourceDefinitionApplyStep(
    string ResourceId,
    ResourceTypeId TypeId,
    ResourceDefinitionApplyStepKind Kind,
    string Description,
    ResourceDefinition? Definition = null);

public sealed record ResourceDefinitionApplyPlan(
    ResourceDefinitionProjection Resource,
    IReadOnlyList<ResourceDefinitionApplyStep> Steps,
    IReadOnlyList<ResourceDefinitionDiagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(diagnostic =>
        diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error);
}

public sealed record ResourceDefinitionGraphApplyPlan(
    IReadOnlyList<ResourceDefinitionApplyPlan> Resources,
    IReadOnlyList<ResourceDefinitionDiagnostic> Diagnostics)
{
    public bool HasErrors =>
        Diagnostics.Any(diagnostic => diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error) ||
        Resources.Any(resource => resource.HasErrors);

    public IEnumerable<ResourceDefinitionApplyStep> Steps =>
        Resources.SelectMany(resource => resource.Steps);
}

public sealed class ResourceDefinitionGraphApplyPlanner(
    IEnumerable<IResourceDefinitionApplyProvider> applyProviders)
{
    private readonly IReadOnlyList<IResourceDefinitionApplyProvider> _applyProviders =
        applyProviders.ToArray();

    public async ValueTask<ResourceDefinitionGraphApplyPlan> PlanApplyAsync(
        ResourceDefinitionGraphValidationPipelineResult validationResult,
        ResourceDefinitionApplyContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(validationResult);
        ArgumentNullException.ThrowIfNull(context);

        if (validationResult.HasErrors)
        {
            return new([], FlattenDiagnostics(validationResult));
        }

        var resourcePlans = new List<ResourceDefinitionApplyPlan>();
        var diagnostics = new List<ResourceDefinitionDiagnostic>();

        foreach (var resource in validationResult.Resources)
        {
            var provider = _applyProviders.FirstOrDefault(provider =>
                provider.TypeId == resource.Resource.TypeDefinition.TypeId &&
                provider.CanPlan(resource.Projection));

            if (provider is null)
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    ResourceDefinitionDiagnosticCodes.ResourceDefinitionApplyProviderMissing,
                    $"No apply provider is registered for resource type '{resource.Resource.TypeDefinition.TypeId}'.",
                    resource.Resource.Definition.EffectiveResourceId));
                continue;
            }

            resourcePlans.Add(await provider.PlanApplyAsync(
                resource.Projection,
                context,
                cancellationToken));
        }

        return new(resourcePlans, diagnostics);
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
