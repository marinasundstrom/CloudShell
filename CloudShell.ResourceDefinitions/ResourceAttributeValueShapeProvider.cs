namespace CloudShell.ResourceDefinitions;

public interface IResourceAttributeValueShapeProvider
{
    IReadOnlyDictionary<ResourceAttributeValueShapeId, ResourceAttributeValueShapeDefinition> GetAttributeValueShapes();
}
