using System.Text.Json;
using CloudShell.ControlPlane.ResourceModel;
using ResourceModelResource = CloudShell.ResourceModel.Resource;

namespace CloudShell.ControlPlane.Providers;

// Project the enrollment policy explicitly; general collection attributes stay private.
public sealed class DeviceRegistryResourceManagerAttributeProvider :
    IResourceModelResourceManagerAttributeProvider
{
    public IReadOnlyDictionary<string, string>? GetAttributes(ResourceModelResource resource)
    {
        if (resource.Type.TypeId != DeviceRegistryResourceTypeProvider.ResourceTypeId)
        {
            return null;
        }

        var registry = new DeviceRegistryResource(resource);
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [DeviceRegistryResourceTypeProvider.Attributes.TrustedCertificates.ToString()] = JsonSerializer.Serialize(registry.TrustedCertificates),
            [DeviceRegistryResourceTypeProvider.Attributes.AllowedSubjectPrefixes.ToString()] = JsonSerializer.Serialize(registry.AllowedSubjectPrefixes),
            [DeviceRegistryResourceTypeProvider.Attributes.RequiredClaims.ToString()] = JsonSerializer.Serialize(registry.RequiredClaims),
            [DeviceRegistryResourceTypeProvider.Attributes.EnrollmentProfiles.ToString()] = JsonSerializer.Serialize(registry.EnrollmentProfiles)
        };
    }
}
