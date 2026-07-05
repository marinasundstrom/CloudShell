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
        var mqttEndpoint = resource.Attributes.GetString(DeviceRegistryResourceTypeProvider.Attributes.MqttEndpoint);
        if (string.IsNullOrWhiteSpace(endpoint) && string.IsNullOrWhiteSpace(mqttEndpoint))
        {
            return ResourceModelResourceManagerEndpointProjection.Empty;
        }

        var endpoints = new List<ResourceEndpoint>();
        var mappings = new List<ResourceEndpointNetworkMapping>();
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            endpoints.Add(ResourceEndpoint.Contract(
                "registry",
                "http",
                ResourceExposureScope.Local,
                ResourceEndpoint.TryGetPort(endpoint, out var port) ? port : null));
            mappings.Add(ResourceEndpointNetworkMapping.ForEndpoint(
                resource.EffectiveResourceId,
                "registry",
                ToRegistryEndpoint(endpoint, resource.EffectiveResourceId),
                ResourceExposureScope.Local,
                sourceEndpointName: "registry"));
        }

        if (!string.IsNullOrWhiteSpace(mqttEndpoint))
        {
            endpoints.Add(ResourceEndpoint.Contract(
                "mqtt",
                "mqtt",
                ResourceExposureScope.Local,
                ResourceEndpoint.TryGetPort(mqttEndpoint, out var port) ? port : null));
            mappings.Add(ResourceEndpointNetworkMapping.ForEndpoint(
                resource.EffectiveResourceId,
                "mqtt",
                mqttEndpoint,
                ResourceExposureScope.Local,
                sourceEndpointName: "mqtt"));
        }

        return new ResourceModelResourceManagerEndpointProjection(
            Endpoints: endpoints,
            EndpointNetworkMappings: mappings);
    }

    private static string ToRegistryEndpoint(
        string endpoint,
        string resourceId) =>
        $"{endpoint.TrimEnd('/')}/api/device-registries/{Uri.EscapeDataString(resourceId)}";
}
