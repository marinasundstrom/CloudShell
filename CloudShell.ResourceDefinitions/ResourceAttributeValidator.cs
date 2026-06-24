namespace CloudShell.ResourceDefinitions;

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
    ResourceDefinition Definition,
    ResourceClassDefinition ClassDefinition,
    ResourceTypeDefinition TypeDefinition,
    ResourceDefinitionResolutionContext ResolutionContext);
