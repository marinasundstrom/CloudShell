namespace CloudShell.ResourceModel;

public sealed class ResourceDefinitionSchemaValidator
{
    private readonly ResourceDefinitionSchemaCatalog _schemaCatalog;

    public ResourceDefinitionSchemaValidator(ResourceDefinitionSchemaCatalog schemaCatalog)
    {
        ArgumentNullException.ThrowIfNull(schemaCatalog);

        _schemaCatalog = schemaCatalog;
    }

    public ResourceDefinitionSchemaValidationResult Validate(
        ResourceDefinition definition,
        ResourceDefinitionResolutionContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var resolver = new ResourceResolver(
            _schemaCatalog.ResourceClassDefinitions.Values,
            _schemaCatalog.ResourceTypes.Values,
            capabilityAttributeProviders: _schemaCatalog.ResourceCapabilityAttributeProviders.Values);
        var resolved = resolver.Resolve(definition, context);
        var diagnostics = new List<ResourceDefinitionDiagnostic>(resolved.Diagnostics);

        if (_schemaCatalog.TryGetResourceSchema(definition.TypeId, out var schema))
        {
            var attributeDefinitions = ResolveAttributeDefinitions(schema);
            var declaredCapabilities = schema.Capabilities
                .Select(capability => capability.Id)
                .ToHashSet();

            foreach (var capabilityId in resolved.State.CapabilityPayloads.Keys
                .OrderBy(capabilityId => capabilityId.ToString(), StringComparer.OrdinalIgnoreCase))
            {
                if (declaredCapabilities.Contains(capabilityId))
                {
                    continue;
                }

                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    ResourceDefinitionDiagnosticCodes.UnknownCapability,
                    $"Capability '{capabilityId}' is not declared by resource type '{definition.TypeId}' or class '{schema.ClassId}'.",
                    capabilityId));
            }

            foreach (var attributeId in resolved.State.ResourceAttributeValues.Keys
                .OrderBy(attributeId => attributeId.ToString(), StringComparer.OrdinalIgnoreCase))
            {
                if (attributeDefinitions.ContainsKey(attributeId))
                {
                    continue;
                }

                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    ResourceDefinitionDiagnosticCodes.UnknownAttribute,
                    $"Attribute '{attributeId}' is not defined by resource type '{definition.TypeId}', class '{schema.ClassId}', or a declared capability.",
                    attributeId));
            }
        }

        return new(
            resolved.State.ToDefinition(),
            diagnostics);
    }

    private IReadOnlyDictionary<ResourceAttributeId, ResourceAttributeDefinition> ResolveAttributeDefinitions(
        ResourceDefinitionSchema schema)
    {
        var attributeDefinitions = schema.Attributes.ToDictionary(
            attribute => attribute.AttributeId,
            attribute => attribute.Definition);

        return attributeDefinitions;
    }
}

public sealed record ResourceDefinitionSchemaValidationResult(
    ResourceDefinition Definition,
    IReadOnlyList<ResourceDefinitionDiagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(diagnostic =>
        diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error);
}
