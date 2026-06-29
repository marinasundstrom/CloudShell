using CloudShell.Abstractions.ResourceManager;
using ResourceManagerClass = CloudShell.Abstractions.ResourceManager.ResourceClass;

namespace CloudShell.ResourceModel.ResourceManager;

public static class ResourceModelResourceDeclarationBuilderExtensions
{
    public static IResourceBuilder Declare(
        this IResourceGraphBuilder builder,
        IResourceDefinitionBuilder resource,
        string? parentResourceId = null,
        string? resourceGroupId = null,
        IReadOnlyList<string>? dependsOn = null,
        ResourceDeclarationPersistence persistence = ResourceDeclarationPersistence.Transient,
        bool overwritePersistedState = false,
        ResourceManagerClass? resourceClass = null,
        IReadOnlyDictionary<string, string>? attributes = null,
        Action<ResourceDeclaration>? onChanged = null,
        ResourceIdentityBinding? identity = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(resource);

        return builder.Declare(
            ResourceModelResourceProvider.DefaultProviderId,
            resource.EffectiveResourceId,
            parentResourceId,
            resourceGroupId,
            dependsOn,
            persistence,
            overwritePersistedState,
            resourceClass,
            attributes,
            onChanged,
            identity);
    }
}
