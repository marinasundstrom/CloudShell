using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceModel;

namespace CloudShell.ControlPlane.Providers;

public sealed class EventBrokerResourceManagerEndpointProjectionProvider :
    IResourceModelResourceManagerEndpointProjectionProvider
{
    public ResourceModelResourceManagerEndpointProjection? GetEndpointProjection(
        Resource resource)
    {
        if (resource.Type.TypeId != EventBrokerResourceTypeProvider.ResourceTypeId)
        {
            return null;
        }

        var protocols = resource.Attributes.GetObject<EventBrokerProtocolEndpoint[]>(
            EventBrokerResourceTypeProvider.Attributes.Protocols) ?? [];
        if (protocols.Length == 0)
        {
            return ResourceModelResourceManagerEndpointProjection.Empty;
        }

        return new ResourceModelResourceManagerEndpointProjection(
            Endpoints: protocols
                .Where(protocol => !string.IsNullOrWhiteSpace(protocol.Endpoint))
                .Select(protocol => ResourceEndpoint.Contract(
                    protocol.Name,
                    protocol.Protocol,
                    ResourceExposureScope.Local,
                    ResourceEndpoint.TryGetPort(protocol.Endpoint, out var port) ? port : null))
                .ToArray(),
            EndpointNetworkMappings: protocols
                .Where(protocol => !string.IsNullOrWhiteSpace(protocol.Endpoint))
                .Select(protocol => ResourceEndpointNetworkMapping.ForEndpoint(
                    resource.EffectiveResourceId,
                    protocol.Name,
                    protocol.Endpoint,
                    ResourceExposureScope.Local,
                    sourceEndpointName: protocol.Name))
                .ToArray());
    }
}
