using ResourceModelResource = CloudShell.ResourceDefinitions.Resource;

namespace CloudShell.ResourceDefinitions.ResourceManager;

public interface IResourceModelResourceManagerEndpointProjectionProvider
{
    ResourceModelResourceManagerEndpointProjection? GetEndpointProjection(
        ResourceModelResource resource);
}
