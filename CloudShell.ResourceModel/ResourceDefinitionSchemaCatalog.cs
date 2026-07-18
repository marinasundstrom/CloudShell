namespace CloudShell.ResourceModel;

public sealed class ResourceDefinitionSchemaCatalog
{
    public static ResourceDefinitionSchemaCatalog Empty { get; } = new();

    public ResourceDefinitionSchemaCatalog(
        IEnumerable<ResourceTypeDefinition>? resourceTypes = null,
        IEnumerable<IResourceCapabilityAttributeProvider>? capabilityAttributeProviders = null,
        IEnumerable<ResourceClassDefinition>? resourceClassDefinitions = null)
    {
        ResourceClassDefinitions = resourceClassDefinitions?
            .ToDictionary(
                resourceClass => resourceClass.ClassId,
                resourceClass => resourceClass)
            ?? new Dictionary<ResourceClassId, ResourceClassDefinition>();

        ResourceTypes = resourceTypes?
            .ToDictionary(
                resourceType => resourceType.TypeId,
                resourceType => resourceType)
            ?? new Dictionary<ResourceTypeId, ResourceTypeDefinition>();

        ResourceCapabilityAttributeProviders = capabilityAttributeProviders?
            .ToDictionary(
                provider => provider.CapabilityId,
                provider => provider)
            ?? new Dictionary<ResourceCapabilityId, IResourceCapabilityAttributeProvider>();
    }

    public IReadOnlyDictionary<ResourceClassId, ResourceClassDefinition> ResourceClassDefinitions { get; }

    public IReadOnlyDictionary<ResourceTypeId, ResourceTypeDefinition> ResourceTypes { get; }

    public IReadOnlyDictionary<ResourceCapabilityId, IResourceCapabilityAttributeProvider>
        ResourceCapabilityAttributeProviders { get; }

    public IReadOnlyDictionary<ResourceAttributeId, ResourceAttributeDefinition>? ResolveAttributeDefinitions(
        ResourceTypeId? resourceTypeId)
    {
        if (resourceTypeId is null ||
            !ResourceTypes.TryGetValue(resourceTypeId.Value, out var resourceType))
        {
            return null;
        }

        var attributes = new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>(
            ResourceClassDefinitions.TryGetValue(resourceType.ClassId, out var resourceClass)
                ? resourceClass.Attributes ?? new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>()
                : new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>());

        if (resourceType.Attributes is not null)
        {
            foreach (var attribute in resourceType.Attributes)
            {
                attributes[attribute.Key] = attribute.Value;
            }
        }

        foreach (var capability in resourceType.Capabilities ?? [])
        {
            if (!ResourceCapabilityAttributeProviders.TryGetValue(
                    capability.Id,
                    out var attributeProvider))
            {
                continue;
            }

            foreach (var attribute in attributeProvider.AttributeDefinitions)
            {
                attributes.TryAdd(attribute.Key, attribute.Value);
            }
        }

        return attributes;
    }

    public ResourceAttributePathResolver CreateAttributePathResolver(ResourceTypeId? resourceTypeId) =>
        ResourceAttributePathResolver.FromDefinitions(ResolveAttributeDefinitions(resourceTypeId));
}
