using CloudShell.Abstractions.ResourceManager;
using ResourceModelResource = CloudShell.ResourceModel.Resource;

namespace CloudShell.ControlPlane.ResourceModel;

public interface IResourceModelResourceManagerObservabilityProvider
{
    ResourceObservability? GetObservability(ResourceModelResource resource);
}
