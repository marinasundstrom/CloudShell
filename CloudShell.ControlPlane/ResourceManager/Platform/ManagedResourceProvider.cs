using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.ResourceManager.Platform;

public sealed class ManagedResourceProvider : IResourceProvider
{
    public string Id => "managed";

    public string DisplayName => "Managed";

    public IReadOnlyList<Resource> GetResources() =>
    [
        new(
            "postgres-main",
            "postgres-main",
            "PostgreSQL",
            DisplayName,
            "westeurope",
            ResourceState.Running,
            [ResourceEndpoint.Contract("postgres", "tcp", ResourceExposureScope.Private)],
            "16.4",
            DateTimeOffset.Now.AddMinutes(-3),
            [],
            EndpointNetworkMappings:
            [
                CreateEndpointNetworkMapping("postgres-main", "postgres", "postgres://main.internal")
            ]),
        new(
            "redis-cache",
            "redis-cache",
            "Redis",
            DisplayName,
            "westeurope",
            ResourceState.Starting,
            [ResourceEndpoint.Contract("redis", "tcp", ResourceExposureScope.Private)],
            "7.2",
            DateTimeOffset.Now.AddSeconds(-45),
            [],
            EndpointNetworkMappings:
            [
                CreateEndpointNetworkMapping("redis-cache", "redis", "redis://cache.internal")
            ]),
        new(
            "service-bus",
            "service-bus",
            "Message Broker",
            DisplayName,
            "northeurope",
            ResourceState.Running,
            [ResourceEndpoint.Contract("broker", "amqp", ResourceExposureScope.Private)],
            "premium",
            DateTimeOffset.Now.AddMinutes(-7),
            [],
            EndpointNetworkMappings:
            [
                CreateEndpointNetworkMapping("service-bus", "broker", "sb://events.internal")
            ])
    ];

    private static ResourceEndpointNetworkMapping CreateEndpointNetworkMapping(
        string resourceId,
        string endpointName,
        string address) =>
        ResourceEndpointNetworkMapping.ForEndpoint(
            resourceId,
            endpointName,
            address,
            ResourceExposureScope.Private,
            sourceEndpointName: endpointName);
}
