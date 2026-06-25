using ResourceManagerState = CloudShell.Abstractions.ResourceManager.ResourceState;
using ResourceModelResource = CloudShell.ResourceDefinitions.Resource;

namespace CloudShell.ResourceDefinitions.ResourceManager;

public interface IResourceModelResourceManagerStateProvider
{
    ResourceManagerState? GetState(ResourceModelResource resource);
}
