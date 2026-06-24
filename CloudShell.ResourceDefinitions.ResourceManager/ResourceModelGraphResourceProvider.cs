using CloudShell.Abstractions.ResourceManager;
using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;

namespace CloudShell.ResourceDefinitions.ResourceManager;

public sealed class ResourceModelGraphResourceProvider :
    IResourceProvider,
    IResourceModelDiagnosticProvider
{
    private readonly Func<ResourceGraphSnapshot> _resolveSnapshot;
    private readonly ResourceResolver _resolver;
    private readonly ResourceDefinitionResolutionContext _resolutionContext;
    private readonly ResourceModelResourceManagerProjectionOptions _projectionOptions;

    public ResourceModelGraphResourceProvider(
        string id,
        string displayName,
        Func<ResourceGraphSnapshot> resolveSnapshot,
        ResourceResolver resolver,
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
        _resolutionContext = resolutionContext ?? ResourceDefinitionResolutionContext.Empty;
        _projectionOptions = projectionOptions ?? new ResourceModelResourceManagerProjectionOptions();
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
                _projectionOptions))
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
}
