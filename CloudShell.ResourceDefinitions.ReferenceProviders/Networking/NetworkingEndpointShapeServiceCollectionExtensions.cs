using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public static class NetworkingEndpointShapeServiceCollectionExtensions
{
    public static IServiceCollection AddNetworkingEndpointGraphShapes(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceAttributeValueShapeProvider, NetworkingEndpointShapeProvider>());

        return services;
    }
}
