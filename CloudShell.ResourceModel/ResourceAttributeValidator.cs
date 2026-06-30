namespace CloudShell.ResourceModel;

public interface IResourceAttributeValidator
{
    bool CanValidate(
        ResourceAttributeResolution attribute,
        ResourceAttributeValidationContext context);

    ResourceDefinitionValidationResult Validate(
        ResourceAttributeResolution attribute,
        ResourceAttributeValidationContext context);
}

public sealed record ResourceAttributeValidationContext(
    ResourceState State,
    ResourceClassDefinition ClassDefinition,
    ResourceTypeDefinition TypeDefinition,
    ResourceDefinitionResolutionContext ResolutionContext);
