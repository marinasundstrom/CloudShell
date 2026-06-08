using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.ResourceManager;

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
            [ResourceEndpoint.FromAddress("postgres", "postgres://main.internal", "tcp", ResourceExposureScope.Private)],
            "16.4",
            DateTimeOffset.Now.AddMinutes(-3),
            []),
        new(
            "redis-cache",
            "redis-cache",
            "Redis",
            DisplayName,
            "westeurope",
            ResourceState.Starting,
            [ResourceEndpoint.FromAddress("redis", "redis://cache.internal", "tcp", ResourceExposureScope.Private)],
            "7.2",
            DateTimeOffset.Now.AddSeconds(-45),
            []),
        new(
            "service-bus",
            "service-bus",
            "Message Broker",
            DisplayName,
            "northeurope",
            ResourceState.Running,
            [ResourceEndpoint.FromAddress("broker", "sb://events.internal", "amqp", ResourceExposureScope.Private)],
            "premium",
            DateTimeOffset.Now.AddMinutes(-7),
            [])
    ];
}
