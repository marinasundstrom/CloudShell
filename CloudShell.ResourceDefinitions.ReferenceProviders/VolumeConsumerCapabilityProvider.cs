using System.Text.Json;

namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class VolumeConsumerCapabilityProvider :
    IResourceCapabilityProvider,
    IResourceCapabilityProjector
{
    public static readonly ResourceCapabilityId CapabilityIdValue = "storage.volumeConsumer";

    public ResourceCapabilityId CapabilityId => CapabilityIdValue;

    public bool CanValidate(
        Resource resource,
        ResourceCapabilityResolution capability) =>
        resource.Type.TypeId == ExecutableApplicationResourceTypeProvider.ResourceTypeId;

    public ValueTask<ResourceDefinitionValidationResult> ValidateAsync(
        Resource resource,
        ResourceCapabilityResolution capability,
        ResourceProviderContext context,
        CancellationToken cancellationToken = default)
    {
        var definition = capability.Payload.Deserialize<VolumeConsumerDefinition>();
        var diagnostics = new List<ResourceDefinitionDiagnostic>();

        if (definition?.Mounts is not { Count: > 0 })
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                "storage.volumeConsumer.mountsRequired",
                "At least one volume mount is required.",
                capability.Id));
        }

        return ValueTask.FromResult(
            ResourceDefinitionValidationResult.FromDiagnostics(diagnostics));
    }

    public bool CanProject(
        Resource resource,
        ResourceCapabilityResolution capability) =>
        CanValidate(resource, capability);

    public ValueTask<IResourceCapabilityProjection> ProjectAsync(
        Resource resource,
        ResourceCapabilityResolution capability,
        ResourceCapabilityProjectionContext context,
        CancellationToken cancellationToken = default)
    {
        var definition = capability.Payload.Deserialize<VolumeConsumerDefinition>();

        return ValueTask.FromResult<IResourceCapabilityProjection>(
            new VolumeConsumerCapability(resource, definition?.Mounts ?? []));
    }
}

public sealed class VolumeConsumerCapability(
    Resource resource,
    IReadOnlyList<VolumeMountDefinition> mounts) : IResourceCapabilityProjection
{
    public Resource Resource { get; } = resource;

    public ResourceCapabilityId CapabilityId => VolumeConsumerCapabilityProvider.CapabilityIdValue;

    public IReadOnlyList<VolumeMountDefinition> Mounts { get; } = mounts;

    public ResourceChangeSet AddMount(VolumeMountDefinition mount)
    {
        var updatedMounts = Mounts
            .Concat([mount])
            .ToArray();

        using var changes = Resource.CreateChangeContext();
        changes.SetCapability(
            CapabilityId,
            ResourceDefinitionJson.FromValue(new VolumeConsumerDefinition(updatedMounts)));
        return changes.ApplyChanges();
    }
}

public sealed record VolumeConsumerDefinition(
    IReadOnlyList<VolumeMountDefinition> Mounts);

public sealed record VolumeMountDefinition(
    string Volume,
    string TargetPath,
    bool ReadOnly = false);
