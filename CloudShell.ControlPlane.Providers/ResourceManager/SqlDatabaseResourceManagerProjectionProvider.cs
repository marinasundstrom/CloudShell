using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceModel;
using ResourceModelResource = CloudShell.ResourceModel.Resource;

namespace CloudShell.ControlPlane.Providers;

public sealed class SqlDatabaseResourceManagerProjectionProvider :
    IResourceModelResourceManagerAttributeProvider
{
    public IReadOnlyDictionary<string, string>? GetAttributes(ResourceModelResource resource)
    {
        if (resource.Type.TypeId != SqlDatabaseResourceTypeProvider.ResourceTypeId ||
            !SqlDatabaseResourceTypeProvider.TryGetServerDependencyResourceId(resource.State, out var serverResourceId))
        {
            return null;
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ResourceAttributeNames.DatabaseServerResourceId] = serverResourceId
        };
    }
}
