using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceModel;
using ResourceModelResource = CloudShell.ResourceModel.Resource;

namespace CloudShell.ControlPlane.Providers;

public sealed class ContainerApplicationResourceModelObservabilityProvider :
    IResourceModelResourceManagerObservabilityProvider
{
    public ResourceObservability? GetObservability(ResourceModelResource resource)
    {
        if (resource.Type.TypeId != ContainerApplicationResourceTypeProvider.ResourceTypeId)
        {
            return null;
        }

        return new ResourceObservability(
            Logs: true,
            Traces: true,
            Metrics: true,
            ServiceName: resource.Name);
    }
}
