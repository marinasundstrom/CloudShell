using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Host.ResourceManager;

public sealed class ManagedResourceProvider : IResourceProvider
{
    public string Id => "managed";

    public string DisplayName => "Managed";

    public IReadOnlyList<CloudResource> GetResources() =>
    [
        new(
            "postgres-main",
            "postgres-main",
            "PostgreSQL",
            DisplayName,
            "westeurope",
            ResourceState.Running,
            [new("postgres", "postgres://main.internal", "tcp", false)],
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
            [new("redis", "redis://cache.internal", "tcp", false)],
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
            [new("broker", "sb://events.internal", "amqp", false)],
            "premium",
            DateTimeOffset.Now.AddMinutes(-7),
            [])
    ];
}
