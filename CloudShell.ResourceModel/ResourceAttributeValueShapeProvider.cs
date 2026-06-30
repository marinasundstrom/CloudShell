namespace CloudShell.ResourceModel;

public interface IResourceAttributeValueShapeProvider
{
    IReadOnlyDictionary<ResourceAttributeValueShapeId, ResourceAttributeValueShapeDefinition> GetAttributeValueShapes();
}
