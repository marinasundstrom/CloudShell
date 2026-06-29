using ResourceModelResource = CloudShell.ResourceModel.Resource;

namespace CloudShell.ResourceModel.ResourceManager;

public interface IResourceModelResourceManagerEndpointProjectionProvider
{
    ResourceModelResourceManagerEndpointProjection? GetEndpointProjection(
        ResourceModelResource resource);
}
