using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.ResourceModel;

public static class ResourceDefinitionBuilderIdentityExtensions
{
    public static ResourceIdentityReference Identity(
        this IResourceDefinitionBuilder resource,
        string? name = null)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return ResourceIdentityReference.ForResource(resource.EffectiveResourceId, name);
    }

    public static ResourcePrincipalReference Principal(
        this IResourceDefinitionBuilder resource,
        string? identityName = null,
        string? displayName = null,
        string? providerId = null)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return resource.Identity(identityName)
            .ToPrincipal(displayName, providerId);
    }

    public static string IdentityClientId(
        this IResourceDefinitionBuilder resource,
        string? identityName = null)
    {
        var identity = resource.Identity(identityName);
        return string.IsNullOrWhiteSpace(identity.Name)
            ? identity.ResourceId
            : $"{identity.ResourceId}/{identity.Name}";
    }
}
