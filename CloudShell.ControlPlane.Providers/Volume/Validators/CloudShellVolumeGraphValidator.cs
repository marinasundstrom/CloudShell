namespace CloudShell.ControlPlane.Providers;

public sealed class CloudShellVolumeGraphValidator : IResourceDefinitionGraphValidator
{
    public ValueTask<ResourceDefinitionValidationResult> ValidateAsync(
        ResourceDefinitionGraphValidationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var diagnostics = new List<ResourceDefinitionDiagnostic>();

        foreach (var resource in context.Resources.Where(resource =>
            resource.Type.TypeId == CloudShellVolumeResourceTypeProvider.ResourceTypeId))
        {
            ValidateStorageReferences(resource, context, diagnostics);
        }

        return ValueTask.FromResult(
            ResourceDefinitionValidationResult.FromDiagnostics(diagnostics));
    }

    private static void ValidateStorageReferences(
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

            if (target.Type.TypeId == StorageResourceTypeProvider.ResourceTypeId)
            {
                continue;
            }

            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                ResourceDefinitionDiagnosticCodes.ResourceCapabilityReferenceInvalid,
                $"CloudShell volume '{resource.EffectiveResourceId}' references resource type '{target.Type.TypeId}', expected '{StorageResourceTypeProvider.ResourceTypeId}'.",
                resource.EffectiveResourceId));
        }
    }
}
