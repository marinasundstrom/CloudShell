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
        CanAttachTo(resource);

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
            new VolumeConsumerCapability(
                context.ExecutionContext ?? new ResourceProjectionExecutionContext(resource),
                definition?.Mounts ?? []));
    }

    internal static bool CanAttachTo(Resource resource) =>
        resource.Type.TypeId == ExecutableApplicationResourceTypeProvider.ResourceTypeId ||
        resource.Type.TypeId == ContainerApplicationResourceTypeProvider.ResourceTypeId;
}

public sealed class VolumeConsumerCapability(
    ResourceProjectionExecutionContext context,
    IReadOnlyList<VolumeMountDefinition> mounts) : IResourceCapabilityProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    public Resource Resource => Context.Resource;

    public ResourceCapabilityId CapabilityId => VolumeConsumerCapabilityProvider.CapabilityIdValue;

    public IReadOnlyList<VolumeMountDefinition> Mounts { get; } = mounts;

    public ResourceChangeSet AddMount(VolumeMountDefinition mount)
    {
        var updatedMounts = Mounts
            .Concat([mount])
            .ToArray();

        using var changes = Context.CreateChangeContext();
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
