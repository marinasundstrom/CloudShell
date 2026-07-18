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
            .OrderBy(
                resourceClass => resourceClass.ClassId.ToString(),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                resourceClass => resourceClass.ClassId,
                resourceClass => resourceClass)
            ?? new Dictionary<ResourceClassId, ResourceClassDefinition>();

        ResourceTypes = resourceTypes?
            .OrderBy(
                resourceType => resourceType.TypeId.ToString(),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                resourceType => resourceType.TypeId,
                resourceType => resourceType)
            ?? new Dictionary<ResourceTypeId, ResourceTypeDefinition>();

        ResourceCapabilityAttributeProviders = capabilityAttributeProviders?
            .OrderBy(
                provider => provider.CapabilityId.ToString(),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                provider => provider.CapabilityId,
                provider => provider)
            ?? new Dictionary<ResourceCapabilityId, IResourceCapabilityAttributeProvider>();
    }

    public IReadOnlyDictionary<ResourceClassId, ResourceClassDefinition> ResourceClassDefinitions { get; }

    public IReadOnlyDictionary<ResourceTypeId, ResourceTypeDefinition> ResourceTypes { get; }

    public IReadOnlyDictionary<ResourceCapabilityId, IResourceCapabilityAttributeProvider>
        ResourceCapabilityAttributeProviders { get; }

    public IReadOnlyList<ResourceDefinitionSchema> GetResourceSchemas() =>
        ResourceTypes.Values
            .Select(CreateResourceSchema)
            .ToArray();

    public bool TryGetResourceSchema(
        ResourceTypeId resourceTypeId,
        out ResourceDefinitionSchema schema)
    {
        if (!ResourceTypes.TryGetValue(resourceTypeId, out var resourceType))
        {
            schema = default!;
            return false;
        }

        schema = CreateResourceSchema(resourceType);
        return true;
    }

    public ResourceDefinitionSchema? GetResourceSchema(ResourceTypeId resourceTypeId) =>
        TryGetResourceSchema(resourceTypeId, out var schema) ? schema : null;

    public IReadOnlyDictionary<ResourceAttributeId, ResourceAttributeDefinition>? ResolveAttributeDefinitions(
        ResourceTypeId? resourceTypeId)
    {
        if (resourceTypeId is null ||
            !ResourceTypes.TryGetValue(resourceTypeId.Value, out var resourceType))
        {
            return null;
        }

        return CreateResourceSchema(resourceType)
            .Attributes
            .ToDictionary(
                attribute => attribute.AttributeId,
                attribute => attribute.Definition);
    }

    public ResourceAttributePathResolver CreateAttributePathResolver(ResourceTypeId? resourceTypeId) =>
        ResourceAttributePathResolver.FromDefinitions(ResolveAttributeDefinitions(resourceTypeId));

    private ResourceDefinitionSchema CreateResourceSchema(ResourceTypeDefinition resourceType)
    {
        ResourceClassDefinitions.TryGetValue(resourceType.ClassId, out var resourceClass);

        var attributes = new Dictionary<ResourceAttributeId, ResourceDefinitionSchemaAttribute>();
        AddAttributes(
            attributes,
            resourceClass?.Attributes,
            ResourceDefinitionValueSource.ClassDefinition,
            resourceClass?.ClassId.ToString());
        AddAttributes(
            attributes,
            resourceType.Attributes,
            ResourceDefinitionValueSource.TypeDefinition,
            resourceType.TypeId.ToString());

        foreach (var capability in SortCapabilities(MergeCapabilities(resourceClass, resourceType)))
        {
            if (!ResourceCapabilityAttributeProviders.TryGetValue(capability.Id, out var attributeProvider))
            {
                continue;
            }

            AddAttributes(
                attributes,
                attributeProvider.AttributeDefinitions,
                ResourceDefinitionValueSource.CapabilityDefinition,
                attributeProvider.CapabilityId.ToString(),
                replaceExisting: false);
        }

        return new(
            resourceType,
            resourceClass,
            attributes.Values
                .OrderBy(attribute => attribute.DocumentPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(attribute => attribute.AttributeId.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            SortCapabilities(MergeCapabilities(resourceClass, resourceType)),
            SortOperations(MergeOperations(resourceClass, resourceType)));
    }

    private static void AddAttributes(
        Dictionary<ResourceAttributeId, ResourceDefinitionSchemaAttribute> target,
        IReadOnlyDictionary<ResourceAttributeId, ResourceAttributeDefinition>? attributes,
        ResourceDefinitionValueSource source,
        string? sourceId,
        bool replaceExisting = true)
    {
        if (attributes is null)
        {
            return;
        }

        foreach (var (attributeId, definition) in attributes
            .OrderBy(attribute => ResolveDocumentPath(attribute.Key, attribute.Value), StringComparer.OrdinalIgnoreCase)
            .ThenBy(attribute => attribute.Key.ToString(), StringComparer.OrdinalIgnoreCase))
        {
            if (!replaceExisting && target.ContainsKey(attributeId))
            {
                continue;
            }

            target[attributeId] = new(
                attributeId,
                definition,
                source,
                sourceId,
                ResolveDocumentPath(attributeId, definition),
                definition.Aliases ?? []);
        }
    }

    private static IReadOnlyList<ResourceCapabilityDeclaration> MergeCapabilities(
        ResourceClassDefinition? resourceClass,
        ResourceTypeDefinition resourceType)
    {
        var capabilities = new Dictionary<ResourceCapabilityId, ResourceCapabilityDeclaration>();
        AddCapabilities(capabilities, resourceClass?.Capabilities);
        AddCapabilities(capabilities, resourceType.Capabilities);
        return capabilities.Values.ToArray();
    }

    private static void AddCapabilities(
        Dictionary<ResourceCapabilityId, ResourceCapabilityDeclaration> target,
        IReadOnlyList<ResourceCapabilityDeclaration>? capabilities)
    {
        foreach (var capability in capabilities ?? [])
        {
            target[capability.Id] = capability;
        }
    }

    private static IReadOnlyList<ResourceOperationDeclaration> MergeOperations(
        ResourceClassDefinition? resourceClass,
        ResourceTypeDefinition resourceType)
    {
        var operations = new Dictionary<ResourceOperationId, ResourceOperationDeclaration>();
        AddOperations(operations, resourceClass?.Operations);
        AddOperations(operations, resourceType.Operations);
        return operations.Values.ToArray();
    }

    private static void AddOperations(
        Dictionary<ResourceOperationId, ResourceOperationDeclaration> target,
        IReadOnlyList<ResourceOperationDeclaration>? operations)
    {
        foreach (var operation in operations ?? [])
        {
            target[operation.Id] = operation;
        }
    }

    private static IReadOnlyList<ResourceCapabilityDeclaration> SortCapabilities(
        IEnumerable<ResourceCapabilityDeclaration> capabilities) =>
        capabilities
            .OrderBy(capability => capability.Id.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<ResourceOperationDeclaration> SortOperations(
        IEnumerable<ResourceOperationDeclaration> operations) =>
        operations
            .OrderBy(operation => operation.Id.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string ResolveDocumentPath(
        ResourceAttributeId attributeId,
        ResourceAttributeDefinition definition) =>
        string.IsNullOrWhiteSpace(definition.Path)
            ? attributeId.ToString()
            : definition.Path.Trim();
}

public sealed record ResourceDefinitionSchema(
    ResourceTypeDefinition TypeDefinition,
    ResourceClassDefinition? ClassDefinition,
    IReadOnlyList<ResourceDefinitionSchemaAttribute> Attributes,
    IReadOnlyList<ResourceCapabilityDeclaration> Capabilities,
    IReadOnlyList<ResourceOperationDeclaration> Operations)
{
    public ResourceTypeId TypeId => TypeDefinition.TypeId;

    public ResourceClassId ClassId => TypeDefinition.ClassId;
}

public sealed record ResourceDefinitionSchemaAttribute(
    ResourceAttributeId AttributeId,
    ResourceAttributeDefinition Definition,
    ResourceDefinitionValueSource Source,
    string? SourceId,
    string DocumentPath,
    IReadOnlyList<string> Aliases);
