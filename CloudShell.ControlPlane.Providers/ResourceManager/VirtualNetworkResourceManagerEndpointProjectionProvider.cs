using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceModel;

namespace CloudShell.ControlPlane.Providers;

public sealed class VirtualNetworkResourceManagerEndpointProjectionProvider :
    IResourceModelResourceManagerEndpointProjectionProvider
{
    public ResourceModelResourceManagerEndpointProjection? GetEndpointProjection(
        Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        if (resource.Type.TypeId != VirtualNetworkResourceTypeProvider.ResourceTypeId)
        {
            return null;
        }

        var endpoints = resource.Attributes
            .GetObject<NetworkingEndpointValue[]>(
                VirtualNetworkResourceTypeProvider.Attributes.Endpoints) ?? [];
        var endpointNetworkMappings = resource.Attributes
            .GetObject<NetworkingEndpointNetworkMappingValue[]>(
                VirtualNetworkResourceTypeProvider.Attributes.EndpointNetworkMappings) ?? [];
        var endpointMappings = resource.Attributes
            .GetObject<NetworkingEndpointMappingValue[]>(
                VirtualNetworkResourceTypeProvider.Attributes.EndpointMappings) ?? [];

        return new ResourceModelResourceManagerEndpointProjection(
            Endpoints: endpoints.Select(ToEndpoint).ToArray(),
            EndpointMappings: endpointMappings
                .Select(ToEndpointMapping)
                .Where(mapping => mapping is not null)
                .Cast<ResourceEndpointMappingDefinition>()
                .ToArray(),
            EndpointNetworkMappings: endpointNetworkMappings
                .Select(ToEndpointNetworkMapping)
                .Where(mapping => mapping is not null)
                .Cast<ResourceEndpointNetworkMapping>()
                .ToArray());
    }

    private static ResourceEndpoint ToEndpoint(NetworkingEndpointValue value) =>
        ResourceEndpoint.Contract(
            value.Name,
            NormalizeProtocol(value.Protocol),
            ParseExposure(value.Exposure),
            value.TargetPort);

    private static ResourceEndpointNetworkMapping? ToEndpointNetworkMapping(
        NetworkingEndpointNetworkMappingValue value)
    {
        if (!value.Target.Resource.TryGetResourceId(out var targetResourceId))
        {
            return null;
        }

        return new(
            value.Id,
            value.Name,
            ResourceEndpointReference.ForEndpoint(
                targetResourceId,
                value.Target.EndpointName),
            value.Address,
            ParseExposure(value.Exposure),
            value.Network?.TryGetResourceId(out var networkResourceId) == true
                ? networkResourceId
                : null,
            value.Provider?.TryGetResourceId(out var providerResourceId) == true
                ? providerResourceId
                : null,
            value.SourceEndpointName);
    }

    private static ResourceEndpointMappingDefinition? ToEndpointMapping(
        NetworkingEndpointMappingValue value)
    {
        if (!value.Source.Resource.TryGetResourceId(out var sourceResourceId) ||
            !value.Target.Resource.TryGetResourceId(out var targetResourceId))
        {
            return null;
        }

        var id = string.IsNullOrWhiteSpace(value.Id)
            ? $"{sourceResourceId}:endpoint-mapping:{value.Source.EndpointName}:{targetResourceId}:{value.Target.EndpointName}"
            : value.Id.Trim();
        return new(
            id,
            string.IsNullOrWhiteSpace(value.Name) ? id : value.Name.Trim(),
            ResourceEndpointReference.ForEndpoint(
                sourceResourceId,
                value.Source.EndpointName),
            ResourceEndpointReference.ForEndpoint(
                targetResourceId,
                value.Target.EndpointName),
            value.Network?.TryGetResourceId(out var networkResourceId) == true
                ? networkResourceId
                : null,
            value.Provider?.TryGetResourceId(out var providerResourceId) == true
                ? providerResourceId
                : null);
    }

    private static string NormalizeProtocol(string value) =>
        value.Trim().ToLowerInvariant();

    private static ResourceExposureScope ParseExposure(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        Enum.TryParse<ResourceExposureScope>(value, ignoreCase: true, out var parsed)
            ? parsed
            : ResourceExposureScope.Public;
}
