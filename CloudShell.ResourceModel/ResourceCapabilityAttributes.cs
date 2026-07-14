namespace CloudShell.ResourceModel;

public interface IResourceCapabilityAttributeProvider
{
    ResourceCapabilityId CapabilityId { get; }

    IReadOnlyDictionary<ResourceAttributeId, ResourceAttributeDefinition> AttributeDefinitions { get; }

    IReadOnlyDictionary<ResourceAttributeValueShapeId, ResourceAttributeValueShapeDefinition> AttributeValueShapes { get; }
}
