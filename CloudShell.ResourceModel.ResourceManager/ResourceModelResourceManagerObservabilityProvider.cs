using CloudShell.Abstractions.ResourceManager;
using ResourceModelResource = CloudShell.ResourceModel.Resource;

namespace CloudShell.ResourceModel.ResourceManager;

public interface IResourceModelResourceManagerObservabilityProvider
{
    ResourceObservability? GetObservability(ResourceModelResource resource);
}
