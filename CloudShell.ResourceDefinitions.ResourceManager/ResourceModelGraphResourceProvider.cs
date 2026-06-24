using CloudShell.Abstractions.ResourceManager;
using ResourceManagerClass = CloudShell.Abstractions.ResourceManager.ResourceClass;
using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;

namespace CloudShell.ResourceDefinitions.ResourceManager;

public sealed class ResourceModelGraphResourceProvider :
    IResourceProvider,
    IResourceModelDiagnosticProvider
{
    private readonly Func<ResourceGraphSnapshot> _resolveSnapshot;
    private readonly ResourceResolver _resolver;
    private readonly ResourceGraphResolver _graphResolver;
    private readonly IReadOnlyList<IResourceGraphDependencyProvider> _dependencyProviders;
    private readonly ResourceDefinitionResolutionContext _resolutionContext;
    private readonly ResourceModelResourceManagerProjectionOptions _projectionOptions;

    public ResourceModelGraphResourceProvider(
        string id,
        string displayName,
        Func<ResourceGraphSnapshot> resolveSnapshot,
        ResourceResolver resolver,
        IEnumerable<IResourceGraphDependencyProvider>? dependencyProviders = null,
        ResourceDefinitionResolutionContext? resolutionContext = null,
        ResourceModelResourceManagerProjectionOptions? projectionOptions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(resolveSnapshot);
        ArgumentNullException.ThrowIfNull(resolver);

        Id = id.Trim();
        DisplayName = displayName.Trim();
        _resolveSnapshot = resolveSnapshot;
        _resolver = resolver;
        _dependencyProviders = (dependencyProviders ?? []).ToArray();
        _graphResolver = new ResourceGraphResolver(_resolver, _dependencyProviders);
        _resolutionContext = resolutionContext ?? ResourceDefinitionResolutionContext.Empty;
        _projectionOptions = (projectionOptions ?? new ResourceModelResourceManagerProjectionOptions()) with
        {
            BridgeProviderId = Id
        };
    }

    public string Id { get; }

    public string DisplayName { get; }

    public IReadOnlyList<ResourceManagerResource> GetResources()
    {
        var snapshot = _resolveSnapshot();

        return snapshot.Resources
            .Select(state => _resolver.Resolve(state, _resolutionContext))
            .Select(resource => ResourceModelResourceManagerMapper.ToResourceManagerResource(
                resource,
                _projectionOptions,
                ResolveDependencies(snapshot, resource).DependencyIds))
            .ToArray();
    }

    public IReadOnlyList<ResourceModelDiagnostic> GetResourceModelDiagnostics()
    {
        var snapshot = _resolveSnapshot();

        return snapshot.Resources
            .Select(state => _resolver.Resolve(state, _resolutionContext))
            .SelectMany(resource =>
                ResourceModelResourceManagerMapper.ToResourceModelDiagnostics(resource)
                    .Concat(ResolveDependencies(snapshot, resource).Diagnostics))
            .ToArray();
    }

    private ResourceModelGraphDependencyProjection ResolveDependencies(
        ResourceGraphSnapshot snapshot,
        Resource resource)
    {
        var dependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var invalidDependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var diagnostics = new List<ResourceModelDiagnostic>();

        foreach (var reference in GetDependencyReferences(resource))
        {
            if (!reference.TryGetResourceId(out var dependency))
            {
                continue;
            }

            var resolution = _graphResolver.ResolveReference(
                snapshot,
                reference,
                _resolutionContext);
            if (HasTypeMismatch(resolution.Diagnostics))
            {
                dependencies.Remove(dependency);
                invalidDependencies.Add(dependency);
                diagnostics.AddRange(resolution.Diagnostics
                    .Where(diagnostic =>
                        diagnostic.Code == ResourceDefinitionDiagnosticCodes.ResourceReferenceTypeMismatch)
                    .Select(diagnostic => ToResourceModelDiagnostic(resource, diagnostic)));
                continue;
            }

            if (!invalidDependencies.Contains(dependency))
            {
                dependencies.Add(dependency);
            }
        }

        return new(dependencies.ToArray(), diagnostics);
    }

    private IReadOnlyList<ResourceReference> GetDependencyReferences(Resource resource)
    {
        var references = new List<ResourceReference>(resource.State.ResourceDependencies);
        var dependencyIds = new HashSet<string>(
            resource.State.ResourceDependencyIds,
            StringComparer.OrdinalIgnoreCase);

        foreach (var provider in _dependencyProviders)
        {
            if (!provider.CanResolveDependencies(resource))
            {
                continue;
            }

            foreach (var reference in provider.GetDependencies(resource))
            {
                if (reference.TryGetResourceId(out var dependency) &&
                    (dependencyIds.Add(dependency) || HasReferenceExpectations(reference)))
                {
                    references.Add(reference);
                }
            }
        }

        return references.ToArray();
    }

    private static bool HasReferenceExpectations(ResourceReference reference) =>
        reference.TypeId is not null ||
        !string.IsNullOrWhiteSpace(reference.ProviderId);

    private static bool HasTypeMismatch(
        IReadOnlyList<ResourceDefinitionDiagnostic> diagnostics) =>
        diagnostics.Any(diagnostic =>
            diagnostic.Code == ResourceDefinitionDiagnosticCodes.ResourceReferenceTypeMismatch);

    private static ResourceModelDiagnostic ToResourceModelDiagnostic(
        Resource resource,
        ResourceDefinitionDiagnostic diagnostic) =>
        new(
            diagnostic.Code,
            string.IsNullOrWhiteSpace(diagnostic.Target)
                ? diagnostic.Message
                : $"{diagnostic.Message} Target: {diagnostic.Target}.",
            resource.EffectiveResourceId,
            resource.Type.TypeId.ToString(),
            ToResourceManagerClass(resource.Class.ClassId),
            ToResourceManagerClass(resource.Class.ClassId),
            "resource model");

    private static ResourceManagerClass ToResourceManagerClass(ResourceClassId classId) =>
        Enum.TryParse<ResourceManagerClass>(classId.ToString(), ignoreCase: true, out var resourceClass)
            ? resourceClass
            : ResourceManagerClass.Generic;

    private sealed record ResourceModelGraphDependencyProjection(
        IReadOnlyList<string> DependencyIds,
        IReadOnlyList<ResourceModelDiagnostic> Diagnostics);
}
