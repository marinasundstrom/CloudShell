namespace CloudShell.ResourceDefinitions;

public interface IResourceGraphDependencyProvider
{
    bool CanResolveDependencies(Resource resource);

    IEnumerable<ResourceReference> GetDependencies(Resource resource);
}

public sealed class ResourceGraphResolver(
    ResourceResolver resourceResolver,
    IEnumerable<IResourceGraphDependencyProvider>? dependencyProviders = null)
{
    private readonly ResourceResolver _resourceResolver =
        resourceResolver ?? throw new ArgumentNullException(nameof(resourceResolver));
    private readonly IReadOnlyList<IResourceGraphDependencyProvider> _dependencyProviders =
        (dependencyProviders ?? []).ToArray();

    public ResourceGraphReferenceResolution ResolveReference(
        ResourceGraphSnapshot snapshot,
        ResourceReference reference,
        ResourceDefinitionResolutionContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(reference);

        var statesById = CreateStateLookup(snapshot);

        return ResolveReference(
            reference,
            statesById,
            context ?? ResourceDefinitionResolutionContext.Empty);
    }

    public ResourceGraphResolutionResult ResolveResourceAndDependencies(
        ResourceGraphSnapshot snapshot,
        string resourceId,
        ResourceDefinitionResolutionContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        var statesById = CreateStateLookup(snapshot);
        var resources = new List<Resource>();
        var diagnostics = new List<ResourceDefinitionDiagnostic>();
        var referenceResolutions = new List<ResourceGraphReferenceResolution>();
        var included = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Resolve(
            resourceId.Trim(),
            statesById,
            context ?? ResourceDefinitionResolutionContext.Empty,
            resources,
            diagnostics,
            referenceResolutions,
            included,
            visiting);

        return new(resources, diagnostics, referenceResolutions);
    }

    private static IReadOnlyDictionary<string, ResourceState> CreateStateLookup(
        ResourceGraphSnapshot snapshot) =>
        snapshot.Resources.ToDictionary(
            state => state.EffectiveResourceId,
            StringComparer.OrdinalIgnoreCase);

    private void Resolve(
        string resourceId,
        IReadOnlyDictionary<string, ResourceState> statesById,
        ResourceDefinitionResolutionContext context,
        List<Resource> resources,
        List<ResourceDefinitionDiagnostic> diagnostics,
        List<ResourceGraphReferenceResolution> referenceResolutions,
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

        foreach (var reference in GetDependencyReferences(state, resource))
        {
            var referenceResolution = ResolveReference(reference, statesById, context);

            if (referenceResolution.Resource is null)
            {
                referenceResolutions.Add(referenceResolution);
                diagnostics.AddRange(referenceResolution.Diagnostics);
                continue;
            }

            var dependencyResourceId = referenceResolution.Resource.EffectiveResourceId;
            Resolve(
                dependencyResourceId,
                statesById,
                context,
                resources,
                diagnostics,
                referenceResolutions,
                included,
                visiting);
            referenceResolutions.Add(referenceResolution with
            {
                Resource = resources.FirstOrDefault(resource =>
                    string.Equals(
                        resource.EffectiveResourceId,
                        dependencyResourceId,
                        StringComparison.OrdinalIgnoreCase)),
                Diagnostics = []
            });
        }

        visiting.Remove(resourceId);
    }

    private ResourceGraphReferenceResolution ResolveReference(
        ResourceReference reference,
        IReadOnlyDictionary<string, ResourceState> statesById,
        ResourceDefinitionResolutionContext context)
    {
        if (!reference.TryGetResourceId(out var resourceId))
        {
            return new(reference, null, []);
        }

        if (!statesById.TryGetValue(resourceId, out var state))
        {
            return new(
                reference,
                null,
                [
                    ResourceDefinitionDiagnostic.Error(
                        ResourceDefinitionDiagnosticCodes.ResourceGraphResourceMissing,
                        $"Resource graph state '{resourceId}' was not found.",
                        resourceId)
                ]);
        }

        var resource = _resourceResolver.Resolve(state, context);

        return new(reference, resource, resource.Diagnostics);
    }

    private IReadOnlyList<ResourceReference> GetDependencyReferences(
        ResourceState state,
        Resource resource)
    {
        var dependencies = new List<ResourceReference>(state.ResourceDependencies);
        var dependencyIds = new HashSet<string>(
            state.ResourceDependencyIds,
            StringComparer.OrdinalIgnoreCase);

        foreach (var provider in _dependencyProviders)
        {
            if (!provider.CanResolveDependencies(resource))
            {
                continue;
            }

            foreach (var reference in provider.GetDependencies(resource))
            {
                if (reference.TryGetResourceId(out var dependency))
                {
                    if (dependencyIds.Add(dependency))
                    {
                        dependencies.Add(reference);
                    }
                }
            }
        }

        return dependencies.ToArray();
    }
}

public sealed record ResourceGraphResolutionResult(
    IReadOnlyList<Resource> Resources,
    IReadOnlyList<ResourceDefinitionDiagnostic> Diagnostics,
    IReadOnlyList<ResourceGraphReferenceResolution>? ReferenceResolutions = null)
{
    public IReadOnlyList<ResourceGraphReferenceResolution> ResolvedReferences =>
        ReferenceResolutions ?? [];

    public bool HasErrors => Diagnostics.Any(diagnostic =>
        diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error);

    public Resource? Target => Resources.FirstOrDefault();
}

public sealed record ResourceGraphReferenceResolution(
    ResourceReference Reference,
    Resource? Resource,
    IReadOnlyList<ResourceDefinitionDiagnostic> Diagnostics)
{
    public bool IsResolved => Resource is not null && !HasErrors;

    public bool HasErrors => Diagnostics.Any(diagnostic =>
        diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error);
}
