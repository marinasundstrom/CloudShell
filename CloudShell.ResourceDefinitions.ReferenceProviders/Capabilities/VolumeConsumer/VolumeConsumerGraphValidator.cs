using System.Text.Json;

namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class VolumeConsumerGraphValidator : IResourceDefinitionGraphValidator
{
    public ValueTask<ResourceDefinitionValidationResult> ValidateAsync(
        ResourceDefinitionGraphValidationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var diagnostics = new List<ResourceDefinitionDiagnostic>();

        foreach (var resource in context.Resources)
        {
            var capability = resource.Capabilities.Resolve(
                VolumeConsumerCapabilityProvider.CapabilityIdValue);
            if (capability is null)
            {
                continue;
            }

            var definition = capability.Payload.Deserialize<VolumeConsumerDefinition>();
            foreach (var mount in definition?.Mounts ?? [])
            {
                var mountedResource = context.FindResource(mount.Volume);
                if (mountedResource is null)
                {
                    diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                        ResourceDefinitionDiagnosticCodes.ResourceCapabilityReferenceMissing,
                        $"Volume mount '{mount.Volume}' does not reference a resource in the graph.",
                        resource.EffectiveResourceId));
                    continue;
                }

                if (!IsVolumeResource(mountedResource))
                {
                    diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                        ResourceDefinitionDiagnosticCodes.ResourceCapabilityReferenceInvalid,
                        $"Volume mount '{mount.Volume}' references resource type '{mountedResource.Type.TypeId}', expected a volume resource.",
                        resource.EffectiveResourceId));
                }
            }
        }

        return ValueTask.FromResult(
            ResourceDefinitionValidationResult.FromDiagnostics(diagnostics));
    }

    private static bool IsVolumeResource(Resource resource) =>
        resource.Type.TypeId == LocalVolumeResourceTypeProvider.ResourceTypeId ||
        resource.Type.TypeId == CloudShellVolumeResourceTypeProvider.ResourceTypeId;
}
