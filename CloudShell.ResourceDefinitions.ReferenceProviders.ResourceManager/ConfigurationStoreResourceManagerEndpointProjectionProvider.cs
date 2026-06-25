using CloudShell.Abstractions.ResourceManager;
using CloudShell.ResourceDefinitions.ResourceManager;

namespace CloudShell.ResourceDefinitions.ReferenceProviders.ResourceManager;

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
                    "entries",
                    "http",
                    ResourceExposureScope.Local,
                    ResourceEndpoint.TryGetPort(endpoint, out var port) ? port : null)
            ],
            EndpointNetworkMappings:
            [
                ResourceEndpointNetworkMapping.ForEndpoint(
                    resource.EffectiveResourceId,
                    "entries",
                    ToEntriesEndpoint(endpoint, resource.EffectiveResourceId),
                    ResourceExposureScope.Local,
                    sourceEndpointName: "entries")
            ]);
    }

    private static string ToEntriesEndpoint(
        string endpoint,
        string resourceId) =>
        $"{endpoint.TrimEnd('/')}/api/configuration/stores/{Uri.EscapeDataString(resourceId)}/entries";
}
