namespace CloudShell.ResourceModel;

public static class ResourceDefinitionAttributePaths
{
    public static ResourceDefinitionAttributePathCanonicalizationResult CanonicalizeAttributePaths(
        this ResourceDefinition definition,
        ResourceAttributePathResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(resolver);

        if (definition.Attributes is null || definition.Attributes.Count == 0)
        {
            return new(definition, []);
        }

        var attributes = new Dictionary<ResourceAttributeId, ResourceAttributeValue>();
        var sources = new Dictionary<ResourceAttributeId, ResourceAttributeId>();
        var diagnostics = new List<ResourceDefinitionDiagnostic>();

        foreach (var (attributeId, value) in definition.Attributes)
        {
            var path = attributeId.ToString();
            if (resolver.TryGetConflict(path, out var conflict))
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    ResourceDefinitionDiagnosticCodes.AttributePathAmbiguous,
                    $"Resource attribute path '{path}' is ambiguous between canonical attribute IDs '{string.Join("', '", conflict.AttributeIds)}'.",
                    path));
                continue;
            }

            var canonicalId = resolver.ResolveOrCreate(path);
            if (attributes.ContainsKey(canonicalId))
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    ResourceDefinitionDiagnosticCodes.DuplicateAttributePath,
                    $"Resource attribute path '{path}' resolves to canonical attribute ID '{canonicalId}', which was already set by '{sources[canonicalId]}'.",
                    path));
                continue;
            }

            attributes[canonicalId] = value;
            sources[canonicalId] = attributeId;
        }

        return new(
            definition with
            {
                Attributes = new ResourceAttributeValueMap(attributes)
            },
            diagnostics);
    }
}

public sealed record ResourceDefinitionAttributePathCanonicalizationResult(
    ResourceDefinition Definition,
    IReadOnlyList<ResourceDefinitionDiagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(diagnostic =>
        diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error);
}
