using ResourceManagerState = CloudShell.Abstractions.ResourceManager.ResourceState;
using ResourceModelResource = CloudShell.ResourceModel.Resource;

namespace CloudShell.ResourceModel.ResourceManager;

public interface IResourceModelResourceManagerStateProvider
{
    ResourceManagerState? GetState(ResourceModelResource resource);
}
