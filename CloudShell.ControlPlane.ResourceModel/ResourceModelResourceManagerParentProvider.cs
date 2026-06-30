using ResourceModelResource = CloudShell.ResourceModel.Resource;

namespace CloudShell.ControlPlane.ResourceModel;

public interface IResourceModelResourceManagerParentProvider
{
    string? GetParentResourceId(ResourceModelResource resource);
}
