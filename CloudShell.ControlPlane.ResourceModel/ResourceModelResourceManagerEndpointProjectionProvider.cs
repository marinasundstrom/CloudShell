using ResourceModelResource = CloudShell.ResourceModel.Resource;

namespace CloudShell.ControlPlane.ResourceModel;

public interface IResourceModelResourceManagerEndpointProjectionProvider
{
    ResourceModelResourceManagerEndpointProjection? GetEndpointProjection(
        ResourceModelResource resource);
}
