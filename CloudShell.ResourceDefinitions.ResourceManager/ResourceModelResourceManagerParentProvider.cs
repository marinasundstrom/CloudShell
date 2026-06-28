using ResourceModelResource = CloudShell.ResourceDefinitions.Resource;

namespace CloudShell.ResourceDefinitions.ResourceManager;

public interface IResourceModelResourceManagerParentProvider
{
    string? GetParentResourceId(ResourceModelResource resource);
}
