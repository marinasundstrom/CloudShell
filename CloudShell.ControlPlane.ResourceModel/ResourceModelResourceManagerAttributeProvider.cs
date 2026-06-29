using ResourceModelResource = CloudShell.ResourceModel.Resource;

namespace CloudShell.ControlPlane.ResourceModel;

public interface IResourceModelResourceManagerAttributeProvider
{
    IReadOnlyDictionary<string, string>? GetAttributes(ResourceModelResource resource);
}
