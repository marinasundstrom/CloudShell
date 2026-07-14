using System.Text.Json;

namespace CloudShell.ControlPlane.Providers;

public sealed class VolumeConsumerCapabilityProvider :
    IResourceCapabilityProvider,
    IResourceCapabilityProjector,
    IResourceCapabilityAttributeProvider
{
    public static readonly ResourceCapabilityId CapabilityIdValue = "storage.volumeConsumer";
    private static readonly ResourceAttributeId CapabilityAttributeId =
        ResourceAttributeId.Create(CapabilityIdValue.ToString());

    public ResourceCapabilityId CapabilityId => CapabilityIdValue;

    public IReadOnlyDictionary<ResourceAttributeId, ResourceAttributeDefinition> AttributeDefinitions { get; } =
        new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
        {
            [CapabilityAttributeId] = new(
                Description: "Volume mount declarations used by the volume-consumer capability.",
                ValueType: ResourceAttributeValueType.ComplexType,
                ValueShape: new(
                    new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
                    {
                        ["mounts"] = ResourceAttributeDefinition.Collection(
                            ResourceAttributeValueType.ComplexType,
                            itemShape: new(
                                new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
                                {
                                    ["volume"] = new(
                                        ValueType: ResourceAttributeValueType.String,
                                        Required: true),
                                    ["targetPath"] = new(
                                        ValueType: ResourceAttributeValueType.String,
                                        Required: true),
                                    ["readOnly"] = new(ValueType: ResourceAttributeValueType.Boolean)
                                }),
                            collection: new(MinSize: 1),
                            required: true,
                            requiredMessage: "At least one volume mount is required.")
                    }),
                Path: CapabilityIdValue,
                Aliases:
                [
                    "volumeConsumer"
                ])
        };

    public IReadOnlyDictionary<ResourceAttributeValueShapeId, ResourceAttributeValueShapeDefinition> AttributeValueShapes { get; } =
        new Dictionary<ResourceAttributeValueShapeId, ResourceAttributeValueShapeDefinition>();

    public bool CanValidate(
        Resource resource,
        ResourceCapabilityResolution capability) =>
        capability.Id == CapabilityId &&
        capability.Source == ResourceDefinitionValueSource.ResourceState;

    public ValueTask<ResourceDefinitionValidationResult> ValidateAsync(
        Resource resource,
        ResourceCapabilityResolution capability,
        ResourceProviderContext context,
        CancellationToken cancellationToken = default)
    {
        var definition = capability.Payload.Deserialize<VolumeConsumerDefinition>(
            ResourceDefinitionJson.Options);
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
        var definition = capability.Payload.Deserialize<VolumeConsumerDefinition>(
            ResourceDefinitionJson.Options);

        return ValueTask.FromResult<IResourceCapabilityProjection>(
            new VolumeConsumerCapability(
                context.ExecutionContext ?? new ResourceProjectionExecutionContext(resource),
                definition?.Mounts ?? []));
    }

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
        changes.SetAttribute(
            ResourceAttributeId.Create(CapabilityId.ToString()),
            ResourceAttributeValue.FromObject(new VolumeConsumerDefinition(updatedMounts)));
        return changes.ApplyChanges();
    }
}

public sealed record VolumeConsumerDefinition(
    IReadOnlyList<VolumeMountDefinition> Mounts);

public sealed record VolumeMountDefinition(
    string Volume,
    string TargetPath,
    bool ReadOnly = false);
