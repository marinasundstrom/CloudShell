namespace CloudShell.ControlPlane.Providers;

public sealed class AspNetCoreProjectReferenceGraphValidator : IResourceDefinitionGraphValidator
{
    public ValueTask<ResourceDefinitionValidationResult> ValidateAsync(
        ResourceDefinitionGraphValidationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var diagnostics = new List<ResourceDefinitionDiagnostic>();

        foreach (var resource in context.Resources.Where(resource =>
            resource.Type.TypeId == AspNetCoreProjectResourceTypeProvider.ResourceTypeId))
        {
            ValidateReferences(resource, context, diagnostics);
        }

        return ValueTask.FromResult(
            ResourceDefinitionValidationResult.FromDiagnostics(diagnostics));
    }

    private static void ValidateReferences(
        Resource resource,
        ResourceDefinitionGraphValidationContext context,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        var references = resource.Attributes.GetObject<ResourceReference[]>(
            AspNetCoreProjectResourceTypeProvider.Attributes.References) ?? [];

        foreach (var reference in references)
        {
            if (reference.Relationship != ResourceReferenceRelationships.Reference)
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    ResourceDefinitionDiagnosticCodes.ResourceCapabilityReferenceInvalid,
                    $".NET app '{resource.EffectiveResourceId}' declares service reference '{reference.Value}' with relationship '{reference.Relationship}', expected '{ResourceReferenceRelationships.Reference}'.",
                    resource.EffectiveResourceId));
                continue;
            }

            if (!reference.TryGetResourceId(out var resourceId))
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    ResourceDefinitionDiagnosticCodes.ResourceCapabilityReferenceInvalid,
                    $".NET app '{resource.EffectiveResourceId}' declares service reference '{reference.Value}' with addressing mode '{reference.AddressingMode}', expected '{ResourceReferenceAddressingModes.ResourceId}'.",
                    resource.EffectiveResourceId));
                continue;
            }

            var target = context.FindResource(resourceId);
            if (target is null)
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    ResourceDefinitionDiagnosticCodes.ResourceReferenceMissing,
                    $".NET app '{resource.EffectiveResourceId}' references missing service resource '{resourceId}'.",
                    resource.EffectiveResourceId));
                continue;
            }

            if (reference.TypeId.HasValue &&
                target.Type.TypeId != reference.TypeId.Value)
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    ResourceDefinitionDiagnosticCodes.ResourceReferenceTypeMismatch,
                    $".NET app '{resource.EffectiveResourceId}' references resource '{target.EffectiveResourceId}' with type '{target.Type.TypeId}', expected '{reference.TypeId.Value}'.",
                    resource.EffectiveResourceId));
            }
        }
    }
}
