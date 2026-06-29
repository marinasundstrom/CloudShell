using ResourceManagerState = CloudShell.Abstractions.ResourceManager.ResourceState;
using ResourceModelResource = CloudShell.ResourceModel.Resource;

namespace CloudShell.ControlPlane.ResourceModel;

public interface IResourceModelResourceManagerStateProvider
{
    ResourceManagerState? GetState(ResourceModelResource resource);
}
