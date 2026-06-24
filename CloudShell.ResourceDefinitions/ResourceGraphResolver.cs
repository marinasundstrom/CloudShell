namespace CloudShell.ResourceDefinitions;

public interface IResourceGraphDependencyProvider
{
    bool CanResolveDependencies(Resource resource);

    IEnumerable<string> GetDependencies(Resource resource);
}

public sealed class ResourceGraphResolver(
    ResourceResolver resourceResolver,
    IEnumerable<IResourceGraphDependencyProvider>? dependencyProviders = null)
{
    private readonly ResourceResolver _resourceResolver =
        resourceResolver ?? throw new ArgumentNullException(nameof(resourceResolver));
    private readonly IReadOnlyList<IResourceGraphDependencyProvider> _dependencyProviders =
        (dependencyProviders ?? []).ToArray();

    public ResourceGraphResolutionResult ResolveResourceAndDependencies(
        ResourceGraphSnapshot snapshot,
        string resourceId,
        ResourceDefinitionResolutionContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        var statesById = snapshot.Resources
            .ToDictionary(
                state => state.EffectiveResourceId,
                StringComparer.OrdinalIgnoreCase);
        var resources = new List<Resource>();
        var diagnostics = new List<ResourceDefinitionDiagnostic>();
        var included = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Resolve(
            resourceId.Trim(),
            statesById,
            context ?? ResourceDefinitionResolutionContext.Empty,
            resources,
            diagnostics,
            included,
            visiting);

        return new(resources, diagnostics);
    }

    private void Resolve(
        string resourceId,
        IReadOnlyDictionary<string, ResourceState> statesById,
        ResourceDefinitionResolutionContext context,
        List<Resource> resources,
        List<ResourceDefinitionDiagnostic> diagnostics,
        HashSet<string> included,
        HashSet<string> visiting)
    {
        if (visiting.Contains(resourceId))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                ResourceDefinitionDiagnosticCodes.ResourceDependencyCycle,
                $"Resource graph dependency cycle includes resource '{resourceId}'.",
                resourceId));
            return;
        }

        if (included.Contains(resourceId))
        {
            return;
        }

        if (!statesById.TryGetValue(resourceId, out var state))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                ResourceDefinitionDiagnosticCodes.ResourceGraphResourceMissing,
                $"Resource graph state '{resourceId}' was not found.",
                resourceId));
            return;
        }

        visiting.Add(resourceId);

        var resource = _resourceResolver.Resolve(state, context);
        resources.Add(resource);
        diagnostics.AddRange(resource.Diagnostics);
        included.Add(resourceId);

        foreach (var dependency in ResolveDependencies(state, resource))
        {
            Resolve(
                dependency,
                statesById,
                context,
                resources,
                diagnostics,
                included,
                visiting);
        }

        visiting.Remove(resourceId);
    }

    private IReadOnlyList<string> ResolveDependencies(
        ResourceState state,
        Resource resource)
    {
        var dependencies = new HashSet<string>(
            state.ResourceDependencyIds,
            StringComparer.OrdinalIgnoreCase);

        foreach (var provider in _dependencyProviders)
        {
            if (!provider.CanResolveDependencies(resource))
            {
                continue;
            }

            foreach (var dependency in provider.GetDependencies(resource))
            {
                if (!string.IsNullOrWhiteSpace(dependency))
                {
                    dependencies.Add(dependency.Trim());
                }
            }
        }

        return dependencies.ToArray();
    }
}

public sealed record ResourceGraphResolutionResult(
    IReadOnlyList<Resource> Resources,
    IReadOnlyList<ResourceDefinitionDiagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(diagnostic =>
        diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error);

    public Resource? Target => Resources.FirstOrDefault();
}
