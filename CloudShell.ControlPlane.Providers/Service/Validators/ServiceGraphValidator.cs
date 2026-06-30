namespace CloudShell.ControlPlane.Providers;

public sealed class ServiceGraphValidator : IResourceDefinitionGraphValidator
{
    public ValueTask<ResourceDefinitionValidationResult> ValidateAsync(
        ResourceDefinitionGraphValidationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var diagnostics = new List<ResourceDefinitionDiagnostic>();

        foreach (var resource in context.Resources.Where(resource =>
            resource.Type.TypeId == ServiceResourceTypeProvider.ResourceTypeId))
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
        var hasResolvedTarget = false;
        var hasResolvedReference = false;

        foreach (var reference in resource.State.StartupDependencies)
        {
            if (!reference.TryGetDependsOnResourceId(out var resourceId))
            {
                continue;
            }

            var target = context.FindResource(resourceId);
            if (target is null)
            {
                continue;
            }

            hasResolvedReference = true;
            var expectsNetwork = IsNetworkType(reference.TypeId);
            var resolvesToNetwork = IsNetworkType(target.Type.TypeId);

            if (expectsNetwork && !resolvesToNetwork)
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    ResourceDefinitionDiagnosticCodes.ResourceCapabilityReferenceInvalid,
                    $"Service '{resource.EffectiveResourceId}' references resource type '{target.Type.TypeId}', expected a network resource.",
                    resource.EffectiveResourceId));
                continue;
            }

            if (!resolvesToNetwork)
            {
                hasResolvedTarget = true;
            }
        }

        if (resource.State.StartupDependencies.Count > 0 &&
            hasResolvedReference &&
            !hasResolvedTarget)
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                ResourceDefinitionDiagnosticCodes.ResourceCapabilityReferenceInvalid,
                $"Service '{resource.EffectiveResourceId}' must reference a target resource.",
                resource.EffectiveResourceId));
        }
    }

    private static bool IsNetworkType(ResourceTypeId? typeId) =>
        typeId == NetworkResourceTypeProvider.ResourceTypeId ||
        typeId == VirtualNetworkResourceTypeProvider.ResourceTypeId;
}
