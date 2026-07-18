namespace CloudShell.ResourceModel;

public interface IResourceCapabilityAttributeProvider
{
    ResourceCapabilityId CapabilityId { get; }

    IReadOnlyDictionary<ResourceAttributeId, ResourceAttributeDefinition> AttributeDefinitions { get; }

    IReadOnlyDictionary<ResourceAttributeValueShapeId, ResourceAttributeValueShapeDefinition> AttributeValueShapes { get; }
}

public sealed record ResourceCapabilityAttributeSchema(
    ResourceCapabilityId CapabilityId,
    IReadOnlyDictionary<ResourceAttributeId, ResourceAttributeDefinition> AttributeDefinitions,
    IReadOnlyDictionary<ResourceAttributeValueShapeId, ResourceAttributeValueShapeDefinition>? AttributeValueShapes = null)
    : IResourceCapabilityAttributeProvider
{
    IReadOnlyDictionary<ResourceAttributeValueShapeId, ResourceAttributeValueShapeDefinition>
        IResourceCapabilityAttributeProvider.AttributeValueShapes =>
            AttributeValueShapes ?? new Dictionary<ResourceAttributeValueShapeId, ResourceAttributeValueShapeDefinition>();

    public static ResourceCapabilityAttributeSchema FromProvider(
        IResourceCapabilityAttributeProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        return new(
            provider.CapabilityId,
            provider.AttributeDefinitions,
            provider.AttributeValueShapes);
    }
}
