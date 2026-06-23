using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.ResourceManager.Platform;

public sealed class CloudShellResourceProvider : IResourceProvider
{
    public string Id => "cloudshell";

    public string DisplayName => "CloudShell";

    public IReadOnlyList<Resource> GetResources() =>
    [
        new(
            "api-gateway",
            "api-gateway",
            "Container App",
            DisplayName,
            "westeurope",
            ResourceState.Running,
            [
                CreateEndpoint("public", "https://api.cloudshell.local", "https", ResourceExposureScope.Public),
                CreateEndpoint("internal", "http://api-gateway:8080", "http", ResourceExposureScope.Private)
            ],
            "2026.06.5",
            DateTimeOffset.Now.AddMinutes(-2),
            ["identity", "redis-cache"],
            EndpointNetworkMappings:
            [
                CreateEndpointNetworkMapping("api-gateway", "public", "https://api.cloudshell.local", ResourceExposureScope.Public),
                CreateEndpointNetworkMapping("api-gateway", "internal", "http://api-gateway:8080", ResourceExposureScope.Private)
            ]),
        new(
            "identity",
            "identity",
            "Service",
            DisplayName,
            "westeurope",
            ResourceState.Running,
            [
                CreateEndpoint("public", "https://identity.cloudshell.local", "https", ResourceExposureScope.Public),
                CreateEndpoint("internal", "http://identity:8080", "http", ResourceExposureScope.Private)
            ],
            "2026.06.3",
            DateTimeOffset.Now.AddMinutes(-8),
            ["postgres-main"],
            EndpointNetworkMappings:
            [
                CreateEndpointNetworkMapping("identity", "public", "https://identity.cloudshell.local", ResourceExposureScope.Public),
                CreateEndpointNetworkMapping("identity", "internal", "http://identity:8080", ResourceExposureScope.Private)
            ]),
        new(
            "worker-billing",
            "worker-billing",
            "Worker",
            DisplayName,
            "northeurope",
            ResourceState.Degraded,
            [CreateEndpoint("events", "queue://billing-events", "queue", ResourceExposureScope.Private)],
            "2026.06.1",
            DateTimeOffset.Now.AddMinutes(-18),
            ["service-bus", "postgres-main"],
            EndpointNetworkMappings:
            [
                CreateEndpointNetworkMapping("worker-billing", "events", "queue://billing-events", ResourceExposureScope.Private)
            ]),
        new(
            "otel-collector",
            "otel-collector",
            "OpenTelemetry Collector",
            DisplayName,
            "westeurope",
            ResourceState.Running,
            [
                CreateEndpoint("otlp-grpc", "http://otel-collector:4317", "grpc", ResourceExposureScope.Private),
                CreateEndpoint("otlp-http", "http://otel-collector:4318", "http", ResourceExposureScope.Private)
            ],
            "0.103",
            DateTimeOffset.Now.AddMinutes(-4),
            [],
            EndpointNetworkMappings:
            [
                CreateEndpointNetworkMapping("otel-collector", "otlp-grpc", "http://otel-collector:4317", ResourceExposureScope.Private),
                CreateEndpointNetworkMapping("otel-collector", "otlp-http", "http://otel-collector:4318", ResourceExposureScope.Private)
            ])
    ];

    private static ResourceEndpoint CreateEndpoint(
        string name,
        string address,
        string protocol,
        ResourceExposureScope exposure) =>
        ResourceEndpoint.Contract(
            name,
            protocol,
            exposure,
            ResourceEndpoint.TryGetPort(address, out var port) ? port : null);

    private static ResourceEndpointNetworkMapping CreateEndpointNetworkMapping(
        string resourceId,
        string endpointName,
        string address,
        ResourceExposureScope exposure) =>
        ResourceEndpointNetworkMapping.ForEndpoint(
            resourceId,
            endpointName,
            address,
            exposure,
            sourceEndpointName: endpointName);

}
