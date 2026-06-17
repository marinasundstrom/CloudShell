using CloudShell.Abstractions.Authorization;

namespace CloudShell.Abstractions.ResourceManager;

public enum ResourceWorkloadKind
{
    LocalExecutable,
    AspNetCoreProject,
    ContainerImage,
    ContainerBuild
}

public enum ResourceLifetime
{
    Detached,
    ControlPlaneScoped
}

public sealed record ResourceVolumeMount(
    string VolumeReference,
    string TargetPath,
    bool ReadOnly = false,
    string? Name = null)
{
    public string NormalizedVolumeReference => VolumeReference.Trim();

    public string NormalizedTargetPath => TargetPath.Trim();

    public string? NormalizedName => string.IsNullOrWhiteSpace(Name) ? null : Name.Trim();

    public string RequiredPermission =>
        ReadOnly
            ? StorageVolumeResourceOperationPermissions.MountRead
            : StorageVolumeResourceOperationPermissions.MountWrite;
}

public sealed record ResourceWorkloadConfiguration(
    ResourceWorkloadKind Kind,
    string Name,
    string? ExecutablePath = null,
    string? Arguments = null,
    string? WorkingDirectory = null,
    string? ProjectPath = null,
    string? ProjectArguments = null,
    bool? AspNetCoreHotReload = null,
    string? Image = null,
    string Registry = ContainerRegistryDefaults.Default,
    string? BuildContext = null,
    string? Dockerfile = null,
    string? ContainerHostId = null,
    int Replicas = 1,
    bool ReplicasEnabled = false,
    IReadOnlyList<AppSetting>? AppSettings = null,
    IReadOnlyList<EnvironmentVariableAssignment>? EnvironmentVariables = null,
    IReadOnlyList<ServicePort>? Ports = null,
    ResourceLifetime Lifetime = ResourceLifetime.ControlPlaneScoped,
    ResourceObservability? Observability = null,
    IReadOnlyList<ResourceVolumeMount>? VolumeMounts = null)
{
    public IReadOnlyList<EnvironmentVariableAssignment> WorkloadEnvironmentVariables =>
        EnvironmentVariables ?? [];

    public IReadOnlyList<AppSetting> WorkloadAppSettings =>
        AppSettings ?? [];

    public IReadOnlyList<ServicePort> WorkloadPorts => Ports ?? [];

    public IReadOnlyList<ResourceVolumeMount> WorkloadVolumeMounts => VolumeMounts ?? [];

    public ResourceObservability EffectiveObservability => Observability ?? ResourceObservability.None;
}

// Orchestrator-owned workload grouping used to materialize one stable resource
// into one or more runtime instances. This is not a Resource Manager resource.
public sealed record ResourceOrchestratorService(
    string ResourceId,
    string Name,
    ResourceWorkloadConfiguration Workload,
    IReadOnlyList<string>? DependsOn = null,
    IReadOnlyList<string>? Networks = null,
    IReadOnlyList<ServicePort>? Ports = null,
    IReadOnlyList<ResourceVolumeMount>? VolumeMounts = null)
{
    public int Replicas => Math.Max(1, Workload.Replicas);

    public bool ReplicasEnabled => Workload.ReplicasEnabled;

    public IReadOnlyList<string> ServiceDependencies => DependsOn ?? [];

    public IReadOnlyList<string> ServiceNetworks => Networks ?? [];

    public IReadOnlyList<ServicePort> ServicePorts => Ports ?? Workload.WorkloadPorts;

    public IReadOnlyList<ResourceVolumeMount> ServiceVolumeMounts => VolumeMounts ?? Workload.WorkloadVolumeMounts;
}

public interface ILifetimeBoundResourceBuilder<out TBuilder> : IResourceBuilder
    where TBuilder : IResourceBuilder
{
    TBuilder WithLifetime(ResourceLifetime lifetime);
}

public interface IExecutableResourceBuilder :
    ILifetimeBoundResourceBuilder<IExecutableResourceBuilder>
{
    IExecutableResourceBuilder WithCommand(
        string executablePath,
        string? arguments = null,
        string? workingDirectory = null);

    IExecutableResourceBuilder WithEndpoint(string? endpoint);

    IExecutableResourceBuilder WithEndpointPort(
        string name,
        int targetPort,
        int? port = null,
        string protocol = "http",
        ResourceExposureScope exposure = ResourceExposureScope.Local);

    IExecutableResourceBuilder WithHttpEndpoint(
        int? port = null,
        int targetPort = 80,
        string name = "http");

    IExecutableResourceBuilder WithHttpsEndpoint(
        int? port = null,
        int targetPort = 443,
        string name = "https");

    IExecutableResourceBuilder WithHttpHealthCheck(
        string path,
        string? endpointName = null,
        string name = "health",
        TimeSpan? timeout = null);

    IExecutableResourceBuilder WithHttpProbe(
        ResourceProbeType type,
        string path,
        string? endpointName = null,
        string? name = null,
        TimeSpan? timeout = null);

    IExecutableResourceBuilder WithEnvironment(
        IReadOnlyList<EnvironmentVariableAssignment> environmentVariables);

    IExecutableResourceBuilder WithEnvironment(
        string name,
        string value);

    IExecutableResourceBuilder WithEnvironment(
        string name,
        ConfigurationEntryReference configurationEntry);

    IExecutableResourceBuilder WithEnvironment(
        string name,
        SecretReference secret);

    IExecutableResourceBuilder WithAppSetting(
        string name,
        string value);

    IExecutableResourceBuilder WithAppSetting(
        string name,
        ConfigurationEntryReference configurationEntry);

    IExecutableResourceBuilder WithAppSetting(
        string name,
        SecretReference secret);

    new IExecutableResourceBuilder WithLifetime(ResourceLifetime lifetime);

    IExecutableResourceBuilder WithServiceDiscovery(bool enabled = true);

    IExecutableResourceBuilder WithObservability(bool enabled = true);

    IExecutableResourceBuilder WithOtlpExporter(
        string? endpoint = null,
        string? protocol = null,
        string? headers = null);

    IExecutableResourceBuilder WaitFor(IResourceBuilder resource);

    IExecutableResourceBuilder WaitFor(IEnumerable<IResourceBuilder> resources);

    new IExecutableResourceBuilder DependsOn(string resourceId);

    new IExecutableResourceBuilder DependsOn(IResourceBuilder resource);

    new IExecutableResourceBuilder DependsOn(IEnumerable<string> resourceIds);

    new IExecutableResourceBuilder DependsOn(IEnumerable<IResourceBuilder> resources);

    new IExecutableResourceBuilder WithResourceGroup(string? resourceGroupId);

    new IExecutableResourceBuilder WithParent(string? parentResourceId);

    new IExecutableResourceBuilder WithParent(IResourceBuilder resource);

    new IExecutableResourceBuilder WithReference(IResourceBuilder resource);

    IExecutableResourceBuilder WithReferences(IEnumerable<IResourceBuilder> resources);

    new IExecutableResourceBuilder Persist(bool overwrite = false);
}

public interface IProjectResourceBuilder :
    ILifetimeBoundResourceBuilder<IProjectResourceBuilder>
{
    IProjectResourceBuilder WithEndpoint(string? endpoint);

    IProjectResourceBuilder WithEndpointPort(
        string name,
        int targetPort,
        int? port = null,
        string protocol = "http",
        ResourceExposureScope exposure = ResourceExposureScope.Local);

    IProjectResourceBuilder WithHttpEndpoint(
        int? port = null,
        int targetPort = 80,
        string name = "http");

    IProjectResourceBuilder WithHttpsEndpoint(
        int? port = null,
        int targetPort = 443,
        string name = "https");

    IProjectResourceBuilder WithLaunchSettingsEndpoints(bool enabled = true);

    IProjectResourceBuilder WithHttpHealthCheck(
        string path,
        string? endpointName = null,
        string name = "health",
        TimeSpan? timeout = null);

    IProjectResourceBuilder WithHttpProbe(
        ResourceProbeType type,
        string path,
        string? endpointName = null,
        string? name = null,
        TimeSpan? timeout = null);

    IProjectResourceBuilder WithEnvironment(
        IReadOnlyList<EnvironmentVariableAssignment> environmentVariables);

    IProjectResourceBuilder WithEnvironment(
        string name,
        string value);

    IProjectResourceBuilder WithEnvironment(
        string name,
        ConfigurationEntryReference configurationEntry);

    IProjectResourceBuilder WithEnvironment(
        string name,
        SecretReference secret);

    IProjectResourceBuilder WithAppSetting(
        string name,
        string value);

    IProjectResourceBuilder WithAppSetting(
        string name,
        ConfigurationEntryReference configurationEntry);

    IProjectResourceBuilder WithAppSetting(
        string name,
        SecretReference secret);

    IProjectResourceBuilder WithApplicationArguments(string? arguments);

    new IProjectResourceBuilder WithLifetime(ResourceLifetime lifetime);

    IProjectResourceBuilder WithServiceDiscovery(bool enabled = true);

    IProjectResourceBuilder WithObservability(bool enabled = true);

    IProjectResourceBuilder WithOtlpExporter(
        string? endpoint = null,
        string? protocol = null,
        string? headers = null);

    IProjectResourceBuilder WaitFor(IResourceBuilder resource);

    IProjectResourceBuilder WaitFor(IEnumerable<IResourceBuilder> resources);

    new IProjectResourceBuilder DependsOn(string resourceId);

    new IProjectResourceBuilder DependsOn(IResourceBuilder resource);

    new IProjectResourceBuilder DependsOn(IEnumerable<string> resourceIds);

    new IProjectResourceBuilder DependsOn(IEnumerable<IResourceBuilder> resources);

    new IProjectResourceBuilder WithResourceGroup(string? resourceGroupId);

    new IProjectResourceBuilder WithParent(string? parentResourceId);

    new IProjectResourceBuilder WithParent(IResourceBuilder resource);

    new IProjectResourceBuilder WithReference(IResourceBuilder resource);

    IProjectResourceBuilder WithReferences(IEnumerable<IResourceBuilder> resources);

    new IProjectResourceBuilder Persist(bool overwrite = false);

    IProjectResourceBuilder AsContainerImage(string image);

    /// <summary>
    /// Sets the registry used when this project is materialized as a container
    /// workload. The default registry is
    /// <c>docker.io</c>.
    /// </summary>
    IProjectResourceBuilder WithRegistry(string registry);

    /// <summary>
    /// Sets the registry credentials used when this project is materialized as
    /// a container workload. The password is read from the named environment
    /// variable at execution time.
    /// </summary>
    IProjectResourceBuilder WithRegistryCredentialsFromEnvironment(
        string username,
        string passwordEnvironmentVariable);

    IProjectResourceBuilder WithContainerBuild(
        string? buildContext,
        string? dockerfile = null);

    IProjectResourceBuilder WithReplicas(int replicas);
}

public interface IContainerResourceBuilder :
    ILifetimeBoundResourceBuilder<IContainerResourceBuilder>
{
    IContainerResourceBuilder WithImage(string image);

    /// <summary>
    /// Sets the registry used for this container app resource. The default
    /// registry is <c>docker.io</c>.
    /// </summary>
    IContainerResourceBuilder WithRegistry(string registry);

    /// <summary>
    /// Sets the registry credentials used for this container app resource. The
    /// password is read from the named environment variable at execution time.
    /// </summary>
    IContainerResourceBuilder WithRegistryCredentialsFromEnvironment(
        string username,
        string passwordEnvironmentVariable);

    IContainerResourceBuilder WithContainerHost(string containerHostId);

    IContainerResourceBuilder WithContainerHost(IResourceBuilder containerEngine);

    IContainerResourceBuilder WithEndpoint(
        string name,
        int targetPort,
        int? port = null,
        string protocol = "tcp",
        ResourceExposureScope exposure = ResourceExposureScope.Local);

    IContainerResourceBuilder WithVolume(
        string volumeReference,
        string targetPath,
        bool readOnly = false,
        string? name = null);

    IContainerResourceBuilder WithVolume(
        IResourceBuilder volume,
        string targetPath,
        bool readOnly = false,
        string? name = null);

    IContainerResourceBuilder WithHttpHealthCheck(
        string path,
        string? endpointName = null,
        string name = "health",
        TimeSpan? timeout = null);

    IContainerResourceBuilder WithHttpProbe(
        ResourceProbeType type,
        string path,
        string? endpointName = null,
        string? name = null,
        TimeSpan? timeout = null);

    IContainerResourceBuilder WithEnvironment(
        IReadOnlyList<EnvironmentVariableAssignment> environmentVariables);

    IContainerResourceBuilder WithEnvironment(
        string name,
        string value);

    IContainerResourceBuilder WithEnvironment(
        string name,
        ConfigurationEntryReference configurationEntry);

    IContainerResourceBuilder WithEnvironment(
        string name,
        SecretReference secret);

    IContainerResourceBuilder WithAppSetting(
        string name,
        string value);

    IContainerResourceBuilder WithAppSetting(
        string name,
        ConfigurationEntryReference configurationEntry);

    IContainerResourceBuilder WithAppSetting(
        string name,
        SecretReference secret);

    IContainerResourceBuilder WithReplicas(int replicas);

    IContainerResourceBuilder WithServiceDiscovery(bool enabled = true);

    IContainerResourceBuilder WithObservability(bool enabled = true);

    IContainerResourceBuilder WithOtlpExporter(
        string? endpoint = null,
        string? protocol = null,
        string? headers = null);

    new IContainerResourceBuilder DependsOn(string resourceId);

    new IContainerResourceBuilder DependsOn(IResourceBuilder resource);

    new IContainerResourceBuilder DependsOn(IEnumerable<string> resourceIds);

    new IContainerResourceBuilder DependsOn(IEnumerable<IResourceBuilder> resources);

    new IContainerResourceBuilder WithResourceGroup(string? resourceGroupId);

    new IContainerResourceBuilder WithParent(string? parentResourceId);

    new IContainerResourceBuilder WithParent(IResourceBuilder resource);

    new IContainerResourceBuilder WithReference(string resourceId);

    new IContainerResourceBuilder WithReference(IResourceBuilder resource);

    new IContainerResourceBuilder WithReferences(IEnumerable<string> resourceIds);

    new IContainerResourceBuilder Persist(bool overwrite = false);
}
