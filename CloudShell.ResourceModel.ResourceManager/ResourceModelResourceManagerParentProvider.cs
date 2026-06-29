using ResourceModelResource = CloudShell.ResourceModel.Resource;

namespace CloudShell.ResourceModel.ResourceManager;

public interface IResourceModelResourceManagerParentProvider
{
    string? GetParentResourceId(ResourceModelResource resource);
}
