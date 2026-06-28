using ResourceModelResource = CloudShell.ResourceDefinitions.Resource;

namespace CloudShell.ResourceDefinitions.ResourceManager;

public interface IResourceModelResourceManagerAttributeProvider
{
    IReadOnlyDictionary<string, string>? GetAttributes(ResourceModelResource resource);
}
