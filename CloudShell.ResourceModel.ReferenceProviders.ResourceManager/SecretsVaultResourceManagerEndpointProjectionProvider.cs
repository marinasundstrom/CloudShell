using CloudShell.Abstractions.ResourceManager;
using CloudShell.ResourceModel.ResourceManager;

namespace CloudShell.ResourceModel.ReferenceProviders.ResourceManager;

public sealed class SecretsVaultResourceManagerEndpointProjectionProvider :
    IResourceModelResourceManagerEndpointProjectionProvider
{
    public ResourceModelResourceManagerEndpointProjection? GetEndpointProjection(
        Resource resource)
    {
        if (resource.Type.TypeId != SecretsVaultResourceTypeProvider.ResourceTypeId)
        {
            return null;
        }

        var endpoint = resource.Attributes.GetString(SecretsVaultResourceTypeProvider.Attributes.Endpoint);
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return ResourceModelResourceManagerEndpointProjection.Empty;
        }

        return new ResourceModelResourceManagerEndpointProjection(
            Endpoints:
            [
                ResourceEndpoint.Contract(
                    "secrets",
                    "http",
                    ResourceExposureScope.Local,
                    ResourceEndpoint.TryGetPort(endpoint, out var port) ? port : null)
            ],
            EndpointNetworkMappings:
            [
                ResourceEndpointNetworkMapping.ForEndpoint(
                    resource.EffectiveResourceId,
                    "secrets",
                    ToSecretsEndpoint(endpoint, resource.EffectiveResourceId),
                    ResourceExposureScope.Local,
                    sourceEndpointName: "secrets")
            ]);
    }

    private static string ToSecretsEndpoint(
        string endpoint,
        string resourceId) =>
        $"{endpoint.TrimEnd('/')}/api/secrets/vaults/{Uri.EscapeDataString(resourceId)}/secrets";
}
