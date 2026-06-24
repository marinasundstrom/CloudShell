using CloudShell.Abstractions.ResourceManager;
using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;

namespace CloudShell.ResourceDefinitions.ResourceManager;

public sealed class ResourceModelGraphResourceProvider :
    IResourceProvider,
    IResourceModelDiagnosticProvider
{
    private readonly Func<ResourceGraphSnapshot> _resolveSnapshot;
    private readonly ResourceResolver _resolver;
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
                ResolveDependencyIds(resource)))
            .ToArray();
    }

    public IReadOnlyList<ResourceModelDiagnostic> GetResourceModelDiagnostics()
    {
        var snapshot = _resolveSnapshot();

        return snapshot.Resources
            .Select(state => _resolver.Resolve(state, _resolutionContext))
            .SelectMany(ResourceModelResourceManagerMapper.ToResourceModelDiagnostics)
            .ToArray();
    }

    private IReadOnlyList<string> ResolveDependencyIds(Resource resource)
    {
        var dependencies = new HashSet<string>(
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
                if (reference.TryGetResourceId(out var dependency))
                {
                    dependencies.Add(dependency);
                }
            }
        }

        return dependencies.ToArray();
    }
}
