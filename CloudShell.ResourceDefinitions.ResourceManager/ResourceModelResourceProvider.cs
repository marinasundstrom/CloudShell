using CloudShell.Abstractions.ResourceManager;
using ResourceModelResource = CloudShell.ResourceDefinitions.Resource;
using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;

namespace CloudShell.ResourceDefinitions.ResourceManager;

public sealed class ResourceModelResourceProvider :
    IResourceProvider,
    IResourceModelDiagnosticProvider
{
    public const string DefaultProviderId = "resource-model";
    public const string DefaultRegion = "local";

    private readonly Func<IReadOnlyList<ResourceModelResource>> _resolveResources;
    private readonly ResourceModelResourceManagerProjectionOptions _options;

    public ResourceModelResourceProvider(
        string id,
        string displayName,
        Func<IReadOnlyList<ResourceModelResource>> resolveResources,
        ResourceModelResourceManagerProjectionOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(resolveResources);

        Id = id.Trim();
        DisplayName = displayName.Trim();
        _resolveResources = resolveResources;
        _options = options ?? new ResourceModelResourceManagerProjectionOptions();
    }

    public string Id { get; }

    public string DisplayName { get; }

    public IReadOnlyList<ResourceManagerResource> GetResources() =>
        _resolveResources()
            .Select(resource => ResourceModelResourceManagerMapper.ToResourceManagerResource(resource, _options))
            .ToArray();

    public IReadOnlyList<ResourceModelDiagnostic> GetResourceModelDiagnostics() =>
        _resolveResources()
            .SelectMany(ResourceModelResourceManagerMapper.ToResourceModelDiagnostics)
            .ToArray();
}
