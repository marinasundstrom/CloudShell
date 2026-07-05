using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceModel;

namespace CloudShell.ControlPlane.Providers;

public sealed class ConfigurationStoreResourceManagerEndpointProjectionProvider :
    IResourceModelResourceManagerEndpointProjectionProvider
{
    public ResourceModelResourceManagerEndpointProjection? GetEndpointProjection(
        Resource resource)
    {
        if (resource.Type.TypeId != ConfigurationStoreResourceTypeProvider.ResourceTypeId)
        {
            return null;
        }

        var endpoint = resource.Attributes.GetString(ConfigurationStoreResourceTypeProvider.Attributes.Endpoint);
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return ResourceModelResourceManagerEndpointProjection.Empty;
        }

        return new ResourceModelResourceManagerEndpointProjection(
            Endpoints:
            [
                ResourceEndpoint.Contract(
                    "settings",
                    "http",
                    ResourceExposureScope.Local,
                    ResourceEndpoint.TryGetPort(endpoint, out var port) ? port : null)
            ],
            EndpointNetworkMappings:
            [
                ResourceEndpointNetworkMapping.ForEndpoint(
                    resource.EffectiveResourceId,
                    "settings",
                    ToSettingsEndpoint(endpoint, resource.EffectiveResourceId),
                    ResourceExposureScope.Local,
                    sourceEndpointName: "settings")
            ]);
    }

    private static string ToSettingsEndpoint(
        string endpoint,
        string resourceId) =>
        $"{endpoint.TrimEnd('/')}/api/configuration/stores/{Uri.EscapeDataString(resourceId)}/settings";
}
