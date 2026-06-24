namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class ContainerApplicationGraphValidator : IResourceDefinitionGraphValidator
{
    public ValueTask<ResourceDefinitionValidationResult> ValidateAsync(
        ResourceDefinitionGraphValidationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var diagnostics = new List<ResourceDefinitionDiagnostic>();

        foreach (var resource in context.Resources.Where(resource =>
            resource.Type.TypeId == ContainerApplicationResourceTypeProvider.ResourceTypeId))
        {
            if (!ContainerApplicationResourceTypeProvider.TryGetContainerHostResourceId(
                    resource.State,
                    out var containerHostResourceId))
            {
                continue;
            }

            var host = context.FindResource(containerHostResourceId);
            if (host is null)
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    ResourceDefinitionDiagnosticCodes.ResourceDependencyMissing,
                    $"Container application '{resource.EffectiveResourceId}' references missing container host resource '{containerHostResourceId}'.",
                    resource.EffectiveResourceId));
                continue;
            }

            if (host.Type.TypeId != ContainerHostResourceTypeProvider.ResourceTypeId)
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    ResourceDefinitionDiagnosticCodes.ResourceCapabilityReferenceInvalid,
                    $"Container application '{resource.EffectiveResourceId}' references resource type '{host.Type.TypeId}', expected '{ContainerHostResourceTypeProvider.ResourceTypeId}'.",
                    resource.EffectiveResourceId));
                continue;
            }

            if (!host.Capabilities.Has(ContainerHostResourceTypeProvider.Capabilities.ContainerImage))
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    ResourceDefinitionDiagnosticCodes.ResourceCapabilityReferenceInvalid,
                    $"Container host '{host.EffectiveResourceId}' does not declare the '{ContainerHostResourceTypeProvider.Capabilities.ContainerImage}' capability required by container applications.",
                    resource.EffectiveResourceId));
            }
        }

        return ValueTask.FromResult(
            ResourceDefinitionValidationResult.FromDiagnostics(diagnostics));
    }
}
