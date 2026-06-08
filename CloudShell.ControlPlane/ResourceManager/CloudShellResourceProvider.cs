using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.ResourceManager;

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
            [new("public", "https://api.cloudshell.local", "https", true), new("internal", "http://api-gateway:8080", "http", false)],
            "2026.06.5",
            DateTimeOffset.Now.AddMinutes(-2),
            ["identity", "redis-cache"]),
        new(
            "identity",
            "identity",
            "Service",
            DisplayName,
            "westeurope",
            ResourceState.Running,
            [new("public", "https://identity.cloudshell.local", "https", true), new("internal", "http://identity:8080", "http", false)],
            "2026.06.3",
            DateTimeOffset.Now.AddMinutes(-8),
            ["postgres-main"]),
        new(
            "worker-billing",
            "worker-billing",
            "Worker",
            DisplayName,
            "northeurope",
            ResourceState.Degraded,
            [new("events", "queue://billing-events", "queue", false)],
            "2026.06.1",
            DateTimeOffset.Now.AddMinutes(-18),
            ["service-bus", "postgres-main"]),
        new(
            "otel-collector",
            "otel-collector",
            "OpenTelemetry Collector",
            DisplayName,
            "westeurope",
            ResourceState.Running,
            [new("otlp-grpc", "http://otel-collector:4317", "grpc", false), new("otlp-http", "http://otel-collector:4318", "http", false)],
            "0.103",
            DateTimeOffset.Now.AddMinutes(-4),
            [])
    ];
}
