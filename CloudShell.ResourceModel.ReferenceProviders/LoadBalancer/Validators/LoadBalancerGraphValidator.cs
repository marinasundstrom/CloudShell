namespace CloudShell.ResourceModel.ReferenceProviders;

public sealed class LoadBalancerGraphValidator : IResourceDefinitionGraphValidator
{
    public ValueTask<ResourceDefinitionValidationResult> ValidateAsync(
        ResourceDefinitionGraphValidationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var diagnostics = new List<ResourceDefinitionDiagnostic>();

        foreach (var resource in context.Resources.Where(resource =>
            resource.Type.TypeId == LoadBalancerResourceTypeProvider.ResourceTypeId))
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

            if (IsHostReference(reference))
            {
                if (!IsHostType(target.Type.TypeId))
                {
                    diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                        ResourceDefinitionDiagnosticCodes.ResourceCapabilityReferenceInvalid,
                        $"Load balancer '{resource.EffectiveResourceId}' references resource type '{target.Type.TypeId}', expected a host resource.",
                        resource.EffectiveResourceId));
                }

                continue;
            }

            if (IsNetworkProviderType(target.Type.TypeId) ||
                target.Class.ClassId == ContainerHostResourceTypeProvider.ClassId)
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    ResourceDefinitionDiagnosticCodes.ResourceCapabilityReferenceInvalid,
                    $"Load balancer '{resource.EffectiveResourceId}' cannot use resource type '{target.Type.TypeId}' as a backend target.",
                    resource.EffectiveResourceId));
            }
        }
    }

    private static bool IsHostReference(ResourceReference reference) =>
        IsHostType(reference.TypeId);

    private static bool IsHostType(ResourceTypeId? typeId) =>
        typeId == ContainerHostResourceTypeProvider.ResourceTypeId ||
        typeId == DockerHostResourceTypeProvider.ResourceTypeId;

    private static bool IsNetworkProviderType(ResourceTypeId typeId) =>
        typeId == LoadBalancerResourceTypeProvider.ResourceTypeId ||
        typeId == NetworkResourceTypeProvider.ResourceTypeId ||
        typeId == VirtualNetworkResourceTypeProvider.ResourceTypeId ||
        typeId == LocalHostNetworkResourceTypeProvider.ResourceTypeId ||
        typeId == MacOSHostNetworkResourceTypeProvider.ResourceTypeId ||
        typeId == DnsZoneResourceTypeProvider.ResourceTypeId ||
        typeId == NameMappingResourceTypeProvider.ResourceTypeId;
}
