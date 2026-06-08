using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Docker;

public sealed record DockerContainerResourceDefinition
{
    public DockerContainerResourceDefinition(
        string id,
        string name,
        string image,
        string dockerResourceId,
        IReadOnlyList<ResourceEndpoint>? endpoints = null,
        IReadOnlyList<string>? dependsOn = null,
        ResourceLifetime lifetime = ResourceLifetime.ControlPlaneScoped,
        IReadOnlyList<ResourceHealthCheck>? healthChecks = null,
        string? registry = null,
        ContainerRegistryCredentials? registryCredentials = null)
    {
        Id = DockerContainerResourceProvider.CreateContainerResourceId(id);
        Name = name;
        Image = image;
        DockerResourceId = DockerContainerResourceProvider.CreateDockerResourceId(dockerResourceId);
        Registry = string.IsNullOrWhiteSpace(registry)
            ? DockerProviderOptions.DefaultRegistry
            : registry.Trim();
        RegistryCredentials = ContainerRegistryCredentials.Normalize(registryCredentials);
        Endpoints = endpoints ?? [];
        DependsOn = dependsOn ?? [];
        Lifetime = lifetime;
        HealthChecks = healthChecks ?? [];
    }

    public string Id { get; init; }

    public string Name { get; init; }

    public string Image { get; init; }

    public string DockerResourceId { get; init; }

    public string Registry { get; init; }

    public ContainerRegistryCredentials? RegistryCredentials { get; init; }

    public IReadOnlyList<ResourceEndpoint> Endpoints { get; init; }

    public IReadOnlyList<string> DependsOn { get; init; }

    public ResourceLifetime Lifetime { get; init; }

    public IReadOnlyList<ResourceHealthCheck> HealthChecks { get; init; }
}
