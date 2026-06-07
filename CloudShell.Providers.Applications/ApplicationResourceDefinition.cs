using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

public sealed record ApplicationResourceDefinition : IEnvironmentVariableConfiguration
{
    public ApplicationResourceDefinition(
        string id,
        string name,
        string executablePath,
        string? arguments = null,
        string? workingDirectory = null,
        string? endpoint = null,
        IReadOnlyList<EnvironmentVariableAssignment>? environmentVariables = null,
        ApplicationLifetime lifetime = ApplicationLifetime.Detached,
        IReadOnlyList<string>? dependsOn = null,
        IReadOnlyList<string>? references = null,
        bool useServiceDiscovery = false,
        string? containerImage = null,
        string? containerBuildContext = null,
        string? containerDockerfile = null,
        int replicas = 1)
    {
        Id = id;
        Name = name;
        ExecutablePath = executablePath;
        Arguments = arguments;
        WorkingDirectory = workingDirectory;
        Endpoint = endpoint;
        EnvironmentVariables = environmentVariables ?? [];
        Lifetime = lifetime;
        DependsOn = dependsOn ?? [];
        References = references ?? [];
        UseServiceDiscovery = useServiceDiscovery;
        ContainerImage = containerImage;
        ContainerBuildContext = containerBuildContext;
        ContainerDockerfile = containerDockerfile;
        Replicas = replicas;
    }

    public string Id { get; init; }

    public string Name { get; init; }

    public string ExecutablePath { get; init; }

    public string? Arguments { get; init; }

    public string? WorkingDirectory { get; init; }

    public string? Endpoint { get; init; }

    public IReadOnlyList<EnvironmentVariableAssignment> EnvironmentVariables { get; init; }

    public ApplicationLifetime Lifetime { get; init; }

    public IReadOnlyList<string> DependsOn { get; init; }

    public IReadOnlyList<string> References { get; init; }

    public bool UseServiceDiscovery { get; init; }

    public string? ContainerImage { get; init; }

    public string? ContainerBuildContext { get; init; }

    public string? ContainerDockerfile { get; init; }

    public int Replicas { get; init; }
}

public enum ApplicationLifetime
{
    Detached,
    ControlPlaneScoped
}
