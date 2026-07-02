using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceModel;

namespace CloudShell.ControlPlane.Providers;

public sealed class JavaScriptAppResourceManagerEndpointProjectionProvider :
    IResourceModelResourceManagerEndpointProjectionProvider
{
    public ResourceModelResourceManagerEndpointProjection? GetEndpointProjection(
        Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        if (resource.Type.TypeId != JavaScriptAppResourceTypeProvider.ResourceTypeId)
        {
            return null;
        }

        var requests = resource.Attributes
            .GetObject<NetworkingEndpointRequestValue[]>(
                JavaScriptAppResourceTypeProvider.Attributes.EndpointRequests) ?? [];

        if (requests.Length == 0)
        {
            return ResourceModelResourceManagerEndpointProjection.Empty;
        }

        var endpoints = requests
            .Where(request =>
                !string.IsNullOrWhiteSpace(request.Name) &&
                !string.IsNullOrWhiteSpace(request.Protocol))
            .Select(request => new ResourceEndpoint(
                request.Name.Trim(),
                NormalizeProtocol(request.Protocol),
                ParseExposure(request.Exposure),
                request.TargetPort ?? request.Port))
            .ToArray();
        var endpointNetworkMappings = requests
            .Select(request => CreateEndpointNetworkMapping(resource, request))
            .Where(mapping => mapping is not null)
            .Cast<ResourceEndpointNetworkMapping>()
            .ToArray();

        return endpoints.Length == 0 && endpointNetworkMappings.Length == 0
            ? ResourceModelResourceManagerEndpointProjection.Empty
            : new ResourceModelResourceManagerEndpointProjection(
                endpoints,
                EndpointNetworkMappings: endpointNetworkMappings);
    }

    private static ResourceEndpointNetworkMapping? CreateEndpointNetworkMapping(
        Resource resource,
        NetworkingEndpointRequestValue request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) ||
            string.IsNullOrWhiteSpace(request.Protocol) ||
            request.Port is not > 0)
        {
            return null;
        }

        var host = FirstNonEmpty(request.Host, request.IpAddress);
        if (host is null)
        {
            return null;
        }

        var protocol = NormalizeProtocol(request.Protocol);
        var address = protocol is "http" or "https"
            ? $"{protocol}://{host}:{request.Port.Value}"
            : $"{host}:{request.Port.Value}";

        return ResourceEndpointNetworkMapping.ForEndpoint(
            resource.EffectiveResourceId,
            request.Name,
            address,
            ParseExposure(request.Exposure),
            request.Network?.TryGetResourceId(out var networkResourceId) == true
                ? networkResourceId
                : null);
    }

    private static string NormalizeProtocol(string protocol) =>
        protocol.Trim().ToLowerInvariant();

    private static ResourceExposureScope ParseExposure(string? exposure) =>
        !string.IsNullOrWhiteSpace(exposure) &&
        Enum.TryParse<ResourceExposureScope>(exposure, ignoreCase: true, out var parsed)
            ? parsed
            : ResourceExposureScope.Local;

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }
}

public sealed class JavaScriptAppResourceManagerObservabilityProvider :
    IResourceModelResourceManagerObservabilityProvider
{
    public ResourceObservability? GetObservability(
        Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return resource.Type.TypeId == JavaScriptAppResourceTypeProvider.ResourceTypeId
            ? new ResourceObservability(
                Logs: true,
                Traces: true,
                Metrics: true,
                ServiceName: resource.Name)
            : null;
    }
}
