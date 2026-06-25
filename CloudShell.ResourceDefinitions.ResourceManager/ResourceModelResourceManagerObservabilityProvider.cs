using CloudShell.Abstractions.ResourceManager;
using ResourceModelResource = CloudShell.ResourceDefinitions.Resource;

namespace CloudShell.ResourceDefinitions.ResourceManager;

public interface IResourceModelResourceManagerObservabilityProvider
{
    ResourceObservability? GetObservability(ResourceModelResource resource);
}
