using CloudShell.ResourceModel;

namespace CloudShell.Abstractions.ResourceManager;

public sealed record ResourceTemplateExportRequest(
    string Name,
    IReadOnlyList<string>? ResourceIds = null,
    string? EnvironmentId = null,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public IReadOnlyList<string> RequestedResourceIds => ResourceIds ?? [];
}

public sealed record ResourceDefinitionSchemaCatalogSnapshot(
    IReadOnlyList<ResourceTypeDefinition> ResourceTypes,
    IReadOnlyList<ResourceCapabilityAttributeSchema> ResourceCapabilityAttributeSchemas,
    IReadOnlyList<ResourceClassDefinition> ResourceClassDefinitions)
{
    public ResourceTemplateSerializerOptions CreateSerializerOptions() =>
        new(new ResourceDefinitionSchemaCatalog(
            ResourceTypes,
            ResourceCapabilityAttributeSchemas,
            ResourceClassDefinitions));
}

public sealed record ResourceTemplateExportResult(
    ResourceTemplate Template,
    IReadOnlyList<ResourceDefinitionDiagnostic> Diagnostics,
    IReadOnlyList<ResourceTypeDefinition>? ResourceTypes = null,
    IReadOnlyList<ResourceCapabilityAttributeSchema>? ResourceCapabilityAttributeSchemas = null,
    IReadOnlyList<ResourceClassDefinition>? ResourceClassDefinitions = null)
{
    public bool HasErrors => Diagnostics.Any(diagnostic =>
        diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error);

    public ResourceTemplateSerializerOptions CreateSerializerOptions(
        IEnumerable<ResourceTypeDefinition>? additionalResourceTypes = null) =>
        new(new ResourceDefinitionSchemaCatalog(
            (ResourceTypes ?? [])
                .Concat(additionalResourceTypes ?? [])
                .GroupBy(resourceType => resourceType.TypeId)
                .Select(group => group.First()),
            ResourceCapabilityAttributeSchemas,
            ResourceClassDefinitions));
}

public sealed record ResourceTemplateApplyResult(
    ResourceTemplate Template,
    bool IsCommitted,
    IReadOnlyList<ResourceDefinitionDiagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(diagnostic =>
        diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error);
}

public sealed record ResourceTemplateApplyRequest(
    ResourceTemplate Template,
    ResourceDefinitionApplyMode Mode = ResourceDefinitionApplyMode.CreateOrUpdate);
