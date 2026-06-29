namespace CloudShell.ControlPlane.Providers;

public sealed class SqlServerGraphValidator : IResourceDefinitionGraphValidator
{
    public ValueTask<ResourceDefinitionValidationResult> ValidateAsync(
        ResourceDefinitionGraphValidationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var diagnostics = new List<ResourceDefinitionDiagnostic>();

        foreach (var resource in context.Resources.Where(resource =>
            resource.Type.TypeId == SqlServerResourceTypeProvider.ResourceTypeId))
        {
            ValidateContainerHostReference(resource, context, diagnostics);
        }

        return ValueTask.FromResult(
            ResourceDefinitionValidationResult.FromDiagnostics(diagnostics));
    }

    private static void ValidateContainerHostReference(
        Resource resource,
        ResourceDefinitionGraphValidationContext context,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (!SqlServerResourceTypeProvider.TryGetContainerHostResourceId(
                resource.State,
                out var containerHostResourceId))
        {
            return;
        }

        var host = context.FindResource(containerHostResourceId);
        if (host is null)
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                ResourceDefinitionDiagnosticCodes.ResourceDependencyMissing,
                $"SQL Server '{resource.EffectiveResourceId}' references missing container host resource '{containerHostResourceId}'.",
                resource.EffectiveResourceId));
            return;
        }

        if (!SqlServerResourceTypeProvider.IsContainerHostResourceType(host.Type.TypeId))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                ResourceDefinitionDiagnosticCodes.ResourceCapabilityReferenceInvalid,
                $"SQL Server '{resource.EffectiveResourceId}' references resource type '{host.Type.TypeId}', expected a container host resource.",
                resource.EffectiveResourceId));
            return;
        }

        if (!host.Capabilities.Has(ContainerHostResourceTypeProvider.Capabilities.ContainerImage))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                ResourceDefinitionDiagnosticCodes.ResourceCapabilityReferenceInvalid,
                $"Container host '{host.EffectiveResourceId}' does not declare the '{ContainerHostResourceTypeProvider.Capabilities.ContainerImage}' capability required by SQL Server resources.",
                resource.EffectiveResourceId));
        }
    }
}
