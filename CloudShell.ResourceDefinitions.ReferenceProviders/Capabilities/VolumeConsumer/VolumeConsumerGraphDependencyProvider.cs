namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class VolumeConsumerGraphDependencyProvider : IResourceGraphDependencyProvider
{
    public bool CanResolveDependencies(Resource resource) =>
        resource.Capabilities.Has(VolumeConsumerCapabilityProvider.CapabilityIdValue);

    public IEnumerable<ResourceReference> GetDependencies(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        var definition = resource.Capabilities.Get<VolumeConsumerDefinition>(
            VolumeConsumerCapabilityProvider.CapabilityIdValue);

        return definition?.Mounts
            .Select(mount => mount.Volume)
            .Where(volume => !string.IsNullOrWhiteSpace(volume))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(volume => ResourceReference.DependsOnResourceId(volume))
            .ToArray() ?? [];
    }
}
