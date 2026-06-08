using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ResourceHost;

public sealed class SampleResourceProvider : IResourceProvider
{
    public const string ProviderId = "sample.resources";

    public string Id => ProviderId;

    public string DisplayName => "Sample Resources";

    public IReadOnlyList<Resource> GetResources() =>
    [
        Create(
            "sample:api",
            "Sample API",
            ResourceState.Running,
            "https://api.sample.local",
            dependsOn: ["sample:database"]),
        Create(
            "sample:database",
            "Sample Database",
            ResourceState.Running,
            "tcp://sample-db:5432"),
        Create(
            "sample:worker",
            "Sample Worker",
            ResourceState.Stopped,
            "queue://sample-work")
    ];

    private Resource Create(
        string id,
        string name,
        ResourceState state,
        string endpoint,
        IReadOnlyList<string>? dependsOn = null) =>
        new(
            id,
            name,
            "Sample service",
            DisplayName,
            "local",
            state,
            [new("default", endpoint, endpoint.Split(':', 2)[0], true)],
            "0.1.0",
            DateTimeOffset.UtcNow,
            dependsOn ?? [],
            TypeId: "sample-service");
}
