namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class NameMappingGraphValidator : IResourceDefinitionGraphValidator
{
    public ValueTask<ResourceDefinitionValidationResult> ValidateAsync(
        ResourceDefinitionGraphValidationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var diagnostics = new List<ResourceDefinitionDiagnostic>();

        foreach (var resource in context.Resources.Where(resource =>
            resource.Type.TypeId == NameMappingResourceTypeProvider.ResourceTypeId))
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
        var hasDnsZone = false;
        var hasTarget = false;

        foreach (var reference in resource.State.ResourceDependencies)
        {
            if (!reference.TryGetResourceId(out var resourceId))
            {
                continue;
            }

            var target = context.FindResource(resourceId);
            var expectsDnsZone = reference.TypeId == DnsZoneResourceTypeProvider.ResourceTypeId;
            var resolvesToDnsZone = target?.Type.TypeId == DnsZoneResourceTypeProvider.ResourceTypeId;

            if (expectsDnsZone && target is not null && !resolvesToDnsZone)
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    ResourceDefinitionDiagnosticCodes.ResourceCapabilityReferenceInvalid,
                    $"Name mapping '{resource.EffectiveResourceId}' references resource type '{target.Type.TypeId}', expected '{DnsZoneResourceTypeProvider.ResourceTypeId}'.",
                    resource.EffectiveResourceId));
                continue;
            }

            if (expectsDnsZone || resolvesToDnsZone)
            {
                hasDnsZone = true;
            }
            else
            {
                hasTarget = true;
            }
        }

        if (!hasDnsZone)
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                ResourceDefinitionDiagnosticCodes.ResourceCapabilityReferenceInvalid,
                $"Name mapping '{resource.EffectiveResourceId}' must reference a DNS zone resource.",
                resource.EffectiveResourceId));
        }

        if (!hasTarget)
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                ResourceDefinitionDiagnosticCodes.ResourceCapabilityReferenceInvalid,
                $"Name mapping '{resource.EffectiveResourceId}' must reference a target resource.",
                resource.EffectiveResourceId));
        }
    }
}
