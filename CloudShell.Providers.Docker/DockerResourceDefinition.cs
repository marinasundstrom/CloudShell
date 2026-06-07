using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Docker;

public sealed record DockerResourceDefinition
{
    public DockerResourceDefinition(
        string id,
        string name,
        IReadOnlyList<ResourceHealthCheck>? healthChecks = null)
    {
        Id = DockerContainerResourceProvider.CreateDockerResourceId(id);
        Name = name;
        HealthChecks = healthChecks ?? [];
    }

    public string Id { get; init; }

    public string Name { get; init; }

    public IReadOnlyList<ResourceHealthCheck> HealthChecks { get; init; }
}
