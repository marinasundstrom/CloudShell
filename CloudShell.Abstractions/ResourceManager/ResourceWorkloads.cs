namespace CloudShell.Abstractions.ResourceManager;

public enum ResourceWorkloadKind
{
    LocalExecutable,
    ContainerImage,
    ContainerBuild
}

public enum ResourceLifetime
{
    Detached,
    ControlPlaneScoped
}

public sealed record ResourceWorkloadConfiguration(
    ResourceWorkloadKind Kind,
    string Name,
    string? ExecutablePath = null,
    string? Arguments = null,
    string? WorkingDirectory = null,
    string? Image = null,
    string? BuildContext = null,
    string? Dockerfile = null,
    string? ContainerEngineId = null,
    int Replicas = 1,
    IReadOnlyList<EnvironmentVariableAssignment>? EnvironmentVariables = null,
    IReadOnlyList<ServicePort>? Ports = null,
    ResourceLifetime Lifetime = ResourceLifetime.ControlPlaneScoped,
    ResourceObservability? Observability = null)
{
    public IReadOnlyList<EnvironmentVariableAssignment> WorkloadEnvironmentVariables =>
        EnvironmentVariables ?? [];

    public IReadOnlyList<ServicePort> WorkloadPorts => Ports ?? [];

    public ResourceObservability EffectiveObservability => Observability ?? ResourceObservability.None;
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

public interface IProjectResourceBuilder : IExecutableResourceBuilder
{
    IProjectResourceBuilder AsContainerImage(string image);

    IProjectResourceBuilder WithContainerBuild(
        string? buildContext,
        string? dockerfile = null);

    IProjectResourceBuilder WithReplicas(int replicas);
}

public interface IContainerResourceBuilder :
    ILifetimeBoundResourceBuilder<IContainerResourceBuilder>
{
    IContainerResourceBuilder WithImage(string image);

    IContainerResourceBuilder WithContainerEngine(string containerEngineId);

    IContainerResourceBuilder WithContainerEngine(IResourceBuilder containerEngine);

    IContainerResourceBuilder WithEndpoint(
        string name,
        int targetPort,
        int? port = null,
        string protocol = "tcp",
        ResourceExposureScope exposure = ResourceExposureScope.Local);

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

    IContainerResourceBuilder WithReplicas(int replicas);

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
