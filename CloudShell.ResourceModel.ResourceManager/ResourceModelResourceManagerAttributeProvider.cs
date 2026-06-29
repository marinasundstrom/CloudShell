using ResourceModelResource = CloudShell.ResourceModel.Resource;

namespace CloudShell.ResourceModel.ResourceManager;

public interface IResourceModelResourceManagerAttributeProvider
{
    IReadOnlyDictionary<string, string>? GetAttributes(ResourceModelResource resource);
}
