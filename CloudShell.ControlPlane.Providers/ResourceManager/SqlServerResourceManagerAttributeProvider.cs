using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceModel;
using CloudShell.ResourceModel;
using ResourceModelResource = CloudShell.ResourceModel.Resource;

namespace CloudShell.ControlPlane.Providers;

public sealed class SqlServerResourceManagerAttributeProvider :
    IResourceModelResourceManagerAttributeProvider
{
    public IReadOnlyDictionary<string, string>? GetAttributes(ResourceModelResource resource)
    {
        if (resource.Type.TypeId != SqlServerResourceTypeProvider.ResourceTypeId)
        {
            return null;
        }

        var volumeConsumer = resource.GetCapability<VolumeConsumerDefinition>(
            VolumeConsumerCapabilityProvider.CapabilityIdValue,
            ResourceDefinitionJson.Options);
        if (volumeConsumer?.Mounts.Any(mount =>
                string.Equals(
                    mount.TargetPath,
                    SqlServerResourceDefaults.DataPath,
                    StringComparison.OrdinalIgnoreCase)) == true)
        {
            return null;
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ResourceAttributeNames.VolumeRequiredMountTargetPaths] = SqlServerResourceDefaults.DataPath
        };
    }
}
