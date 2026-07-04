using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceModel;

namespace CloudShell.ControlPlane.Providers;

public sealed class DeviceRegistryResourceManagerEndpointProjectionProvider :
    IResourceModelResourceManagerEndpointProjectionProvider
{
    public ResourceModelResourceManagerEndpointProjection? GetEndpointProjection(
        Resource resource)
    {
        if (resource.Type.TypeId != DeviceRegistryResourceTypeProvider.ResourceTypeId)
        {
            return null;
        }

        var endpoint = resource.Attributes.GetString(DeviceRegistryResourceTypeProvider.Attributes.Endpoint);
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return ResourceModelResourceManagerEndpointProjection.Empty;
        }

        return new ResourceModelResourceManagerEndpointProjection(
            Endpoints:
            [
                ResourceEndpoint.Contract(
                    "registry",
                    "http",
                    ResourceExposureScope.Local,
                    ResourceEndpoint.TryGetPort(endpoint, out var port) ? port : null)
            ],
            EndpointNetworkMappings:
            [
                ResourceEndpointNetworkMapping.ForEndpoint(
                    resource.EffectiveResourceId,
                    "registry",
                    ToRegistryEndpoint(endpoint, resource.EffectiveResourceId),
                    ResourceExposureScope.Local,
                    sourceEndpointName: "registry")
            ]);
    }

    private static string ToRegistryEndpoint(
        string endpoint,
        string resourceId) =>
        $"{endpoint.TrimEnd('/')}/api/device-registries/{Uri.EscapeDataString(resourceId)}";
}
