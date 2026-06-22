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
        string? containerHostId = null,
        int replicas = 1,
        IReadOnlyList<ServicePort>? endpointPorts = null,
        string? resourceType = null,
        IReadOnlyList<ResourceHealthCheck>? healthChecks = null,
        IReadOnlyList<ResourceRecoveryPolicy>? recoveryPolicies = null,
        ResourceObservability? observability = null,
        string? projectPath = null,
        string? projectArguments = null,
        bool aspNetCoreHotReload = false,
        bool useLaunchSettingsEndpoints = false,
        string? containerRevision = null,
        string? containerRegistry = null,
        ContainerRegistryCredentials? containerRegistryCredentials = null,
        IReadOnlyList<AppSetting>? appSettings = null,
        bool projectContainerBuild = false,
        IReadOnlyList<ResourceVolumeMount>? volumeMounts = null,
        bool replicasEnabled = false,
        IReadOnlyList<SqlServerDatabaseDefinition>? sqlDatabases = null)
    {
        Id = id;
        Name = name;
        ExecutablePath = executablePath;
        Arguments = arguments;
        WorkingDirectory = workingDirectory;
        Endpoint = endpoint;
        AppSettings = appSettings ?? [];
        EnvironmentVariables = environmentVariables ?? [];
        Lifetime = lifetime;
        DependsOn = dependsOn ?? [];
        References = references ?? [];
        UseServiceDiscovery = useServiceDiscovery;
        ContainerImage = containerImage;
        ContainerRegistry = containerRegistry;
        ContainerBuildContext = containerBuildContext;
        ContainerDockerfile = containerDockerfile;
        ProjectContainerBuild = projectContainerBuild;
        ContainerHostId = containerHostId;
        Replicas = replicas;
        ReplicasEnabled = replicasEnabled;
        EndpointPorts = endpointPorts ?? [];
        ResourceType = string.IsNullOrWhiteSpace(resourceType)
            ? ApplicationResourceTypes.ExecutableApplication
            : resourceType;
        HealthChecks = healthChecks ?? [];
        RecoveryPolicies = recoveryPolicies ?? [];
        Observability = observability;
        ProjectPath = projectPath;
        ProjectArguments = projectArguments;
        AspNetCoreHotReload = aspNetCoreHotReload;
        UseLaunchSettingsEndpoints = useLaunchSettingsEndpoints;
        ContainerRevision = containerRevision;
        ContainerRegistryCredentials = containerRegistryCredentials;
        VolumeMounts = volumeMounts ?? [];
        SqlDatabases = sqlDatabases ?? [];
    }

    public string Id { get; init; }

    public string Name { get; init; }

    public string ExecutablePath { get; init; }

    public string? Arguments { get; init; }

    public string? WorkingDirectory { get; init; }

    public string? Endpoint { get; init; }

    public IReadOnlyList<AppSetting> AppSettings { get; init; }

    public IReadOnlyList<EnvironmentVariableAssignment> EnvironmentVariables { get; init; }

    public ApplicationLifetime Lifetime { get; init; }

    public IReadOnlyList<string> DependsOn { get; init; }

    public IReadOnlyList<string> References { get; init; }

    public bool UseServiceDiscovery { get; init; }

    public string? ContainerImage { get; init; }

    public string? ContainerRegistry { get; init; }

    public string? ContainerBuildContext { get; init; }

    public string? ContainerDockerfile { get; init; }

    public bool ProjectContainerBuild { get; init; }

    public string? ContainerHostId { get; init; }

    public int Replicas { get; init; }

    public bool ReplicasEnabled { get; init; }

    public IReadOnlyList<ServicePort> EndpointPorts { get; init; }

    public string ResourceType { get; init; }

    public IReadOnlyList<ResourceHealthCheck> HealthChecks { get; init; }

    public IReadOnlyList<ResourceRecoveryPolicy> RecoveryPolicies { get; init; }

    public ResourceObservability? Observability { get; init; }

    public string? ProjectPath { get; init; }

    public string? ProjectArguments { get; init; }

    public bool AspNetCoreHotReload { get; init; }

    public bool UseLaunchSettingsEndpoints { get; init; }

    public string? ContainerRevision { get; init; }

    public ContainerRegistryCredentials? ContainerRegistryCredentials { get; init; }

    public IReadOnlyList<ResourceVolumeMount> VolumeMounts { get; init; }

    public IReadOnlyList<SqlServerDatabaseDefinition> SqlDatabases { get; init; }
}

public sealed record SqlServerDatabaseDefinition(
    string Name,
    string? DisplayName = null);

public enum ApplicationLifetime
{
    Detached,
    ControlPlaneScoped
}
