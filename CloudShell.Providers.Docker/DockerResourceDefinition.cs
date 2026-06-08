using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Docker;

public sealed record DockerResourceDefinition
{
    public DockerResourceDefinition(
        string id,
        string name,
        IReadOnlyList<ResourceHealthCheck>? healthChecks = null,
        string? registry = null)
    {
        Id = DockerContainerResourceProvider.CreateDockerResourceId(id);
        Name = name;
        Registry = string.IsNullOrWhiteSpace(registry)
            ? DockerProviderOptions.DefaultRegistry
            : registry.Trim();
        HealthChecks = healthChecks ?? [];
    }

    public string Id { get; init; }

    public string Name { get; init; }

    public string Registry { get; init; }

    public IReadOnlyList<ResourceHealthCheck> HealthChecks { get; init; }
}
