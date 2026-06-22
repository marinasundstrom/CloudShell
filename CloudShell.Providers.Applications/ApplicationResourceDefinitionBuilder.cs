using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

public sealed class ApplicationResourceDefinitionBuilder
{
    private readonly string _id;
    private readonly string _name;
    private string _executablePath;
    private string? _arguments;
    private string? _workingDirectory;
    private string? _endpoint;
    private IReadOnlyList<EnvironmentVariableAssignment> _environmentVariables = [];
    private ApplicationLifetime _lifetime = ApplicationLifetime.Detached;
    private IReadOnlyList<string> _dependsOn = [];
    private IReadOnlyList<string> _references = [];
    private bool _useServiceDiscovery;
    private string? _containerImage;
    private string? _containerBuildContext;
    private string? _containerDockerfile;
    private string? _containerHostId;
    private int _replicas = 1;
    private IReadOnlyList<ServicePort> _endpointPorts = [];
    private string _resourceType;
    private IReadOnlyList<ResourceHealthCheck> _healthChecks = [];
    private IReadOnlyList<ResourceRecoveryPolicy> _recoveryPolicies = [];
    private ResourceObservability? _observability;
    private string? _projectPath;
    private string? _projectArguments;
    private bool _aspNetCoreHotReload;
    private bool _useLaunchSettingsEndpoints;
    private string? _containerRevision;
    private string? _containerRegistry;
    private ContainerRegistryCredentials? _containerRegistryCredentials;
    private IReadOnlyList<AppSetting> _appSettings = [];
    private bool _projectContainerBuild;
    private IReadOnlyList<ResourceVolumeMount> _volumeMounts = [];
    private bool _replicasEnabled;
    private IReadOnlyList<SqlServerDatabaseDefinition> _sqlDatabases = [];
    private IReadOnlyList<ResourceLogSource> _logSources = [];

    private ApplicationResourceDefinitionBuilder(
        string id,
        string name,
        string executablePath,
        string resourceType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _id = id;
        _name = name;
        _executablePath = executablePath;
        _resourceType = resourceType;
    }

    public static ApplicationResourceDefinitionBuilder ForExecutable(
        string id,
        string name,
        string executablePath) =>
        new(id, name, executablePath, ApplicationResourceTypes.ExecutableApplication);

    public static ApplicationResourceDefinitionBuilder ForAspNetCoreProject(
        string id,
        string name,
        string projectPath) =>
        new ApplicationResourceDefinitionBuilder(id, name, string.Empty, ApplicationResourceTypes.AspNetCoreProject)
            .WithProjectPath(projectPath);

    public static ApplicationResourceDefinitionBuilder ForContainerImage(
        string id,
        string name,
        string image,
        string resourceType = ApplicationResourceTypes.ContainerApp) =>
        new ApplicationResourceDefinitionBuilder(id, name, string.Empty, resourceType)
            .WithContainerImage(image);

    public ApplicationResourceDefinitionBuilder WithResourceType(string resourceType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceType);
        _resourceType = resourceType;
        return this;
    }

    public ApplicationResourceDefinitionBuilder WithCommand(
        string executablePath,
        string? arguments = null,
        string? workingDirectory = null)
    {
        _executablePath = executablePath;
        _arguments = arguments;
        _workingDirectory = workingDirectory;
        return this;
    }

    public ApplicationResourceDefinitionBuilder WithArguments(string? arguments)
    {
        _arguments = arguments;
        return this;
    }

    public ApplicationResourceDefinitionBuilder WithWorkingDirectory(string? workingDirectory)
    {
        _workingDirectory = workingDirectory;
        return this;
    }

    public ApplicationResourceDefinitionBuilder WithEndpoint(string? endpoint)
    {
        _endpoint = endpoint;
        return this;
    }

    public ApplicationResourceDefinitionBuilder WithEnvironmentVariables(
        IReadOnlyList<EnvironmentVariableAssignment> environmentVariables)
    {
        _environmentVariables = environmentVariables;
        return this;
    }

    public ApplicationResourceDefinitionBuilder WithAppSettings(IReadOnlyList<AppSetting> appSettings)
    {
        _appSettings = appSettings;
        return this;
    }

    public ApplicationResourceDefinitionBuilder WithLifetime(ApplicationLifetime lifetime)
    {
        _lifetime = lifetime;
        return this;
    }

    public ApplicationResourceDefinitionBuilder WithDependencies(IReadOnlyList<string> dependsOn)
    {
        _dependsOn = dependsOn;
        return this;
    }

    public ApplicationResourceDefinitionBuilder WithReferences(IReadOnlyList<string> references)
    {
        _references = references;
        return this;
    }

    public ApplicationResourceDefinitionBuilder WithServiceDiscovery(bool enabled)
    {
        _useServiceDiscovery = enabled;
        return this;
    }

    public ApplicationResourceDefinitionBuilder WithContainerImage(string? image)
    {
        _containerImage = image;
        return this;
    }

    public ApplicationResourceDefinitionBuilder WithContainerBuild(
        string? buildContext,
        string? dockerfile = null,
        bool projectContainerBuild = false)
    {
        _containerBuildContext = buildContext;
        _containerDockerfile = dockerfile;
        _projectContainerBuild = projectContainerBuild;
        return this;
    }

    public ApplicationResourceDefinitionBuilder WithContainerRegistry(string? registry)
    {
        _containerRegistry = registry;
        return this;
    }

    public ApplicationResourceDefinitionBuilder WithContainerRegistryCredentials(
        ContainerRegistryCredentials? credentials)
    {
        _containerRegistryCredentials = credentials;
        return this;
    }

    public ApplicationResourceDefinitionBuilder WithContainerHost(string? containerHostId)
    {
        _containerHostId = containerHostId;
        return this;
    }

    public ApplicationResourceDefinitionBuilder WithReplicas(
        int replicas,
        bool enabled)
    {
        _replicas = replicas;
        _replicasEnabled = enabled;
        return this;
    }

    public ApplicationResourceDefinitionBuilder WithEndpointPorts(IReadOnlyList<ServicePort> endpointPorts)
    {
        _endpointPorts = endpointPorts;
        return this;
    }

    public ApplicationResourceDefinitionBuilder WithHealthChecks(IReadOnlyList<ResourceHealthCheck>? healthChecks)
    {
        _healthChecks = healthChecks ?? [];
        return this;
    }

    public ApplicationResourceDefinitionBuilder WithRecoveryPolicies(IReadOnlyList<ResourceRecoveryPolicy> policies)
    {
        _recoveryPolicies = policies;
        return this;
    }

    public ApplicationResourceDefinitionBuilder WithObservability(ResourceObservability? observability)
    {
        _observability = observability;
        return this;
    }

    public ApplicationResourceDefinitionBuilder WithProjectPath(string? projectPath)
    {
        _projectPath = projectPath;
        return this;
    }

    public ApplicationResourceDefinitionBuilder WithProjectArguments(string? arguments)
    {
        _projectArguments = arguments;
        return this;
    }

    public ApplicationResourceDefinitionBuilder WithAspNetCoreHotReload(bool enabled)
    {
        _aspNetCoreHotReload = enabled;
        return this;
    }

    public ApplicationResourceDefinitionBuilder WithLaunchSettingsEndpoints(bool enabled)
    {
        _useLaunchSettingsEndpoints = enabled;
        return this;
    }

    public ApplicationResourceDefinitionBuilder WithContainerRevision(string? revision)
    {
        _containerRevision = revision;
        return this;
    }

    public ApplicationResourceDefinitionBuilder WithVolumeMounts(IReadOnlyList<ResourceVolumeMount> volumeMounts)
    {
        _volumeMounts = volumeMounts;
        return this;
    }

    public ApplicationResourceDefinitionBuilder WithSqlDatabases(
        IReadOnlyList<SqlServerDatabaseDefinition> databases)
    {
        _sqlDatabases = databases;
        return this;
    }

    public ApplicationResourceDefinitionBuilder WithLogSources(IReadOnlyList<ResourceLogSource> logSources)
    {
        _logSources = logSources;
        return this;
    }

    public ApplicationResourceDefinition Build() =>
        new(
            _id,
            _name,
            _executablePath,
            _arguments,
            _workingDirectory,
            _endpoint,
            _environmentVariables,
            _lifetime,
            _dependsOn,
            _references,
            _useServiceDiscovery,
            _containerImage,
            _containerBuildContext,
            _containerDockerfile,
            _containerHostId,
            _replicas,
            _endpointPorts,
            _resourceType,
            _healthChecks,
            _recoveryPolicies,
            _observability,
            _projectPath,
            _projectArguments,
            _aspNetCoreHotReload,
            _useLaunchSettingsEndpoints,
            _containerRevision,
            _containerRegistry,
            _containerRegistryCredentials,
            _appSettings,
            _projectContainerBuild,
            _volumeMounts,
            _replicasEnabled,
            _sqlDatabases,
            _logSources);
}
