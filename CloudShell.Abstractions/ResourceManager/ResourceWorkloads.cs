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
    ResourceLifetime Lifetime = ResourceLifetime.ControlPlaneScoped)
{
    public IReadOnlyList<EnvironmentVariableAssignment> WorkloadEnvironmentVariables =>
        EnvironmentVariables ?? [];

    public IReadOnlyList<ServicePort> WorkloadPorts => Ports ?? [];
}

public interface ILifetimeBoundResourceBuilder<out TBuilder> : ICloudShellResourceBuilder
    where TBuilder : ICloudShellResourceBuilder
{
    TBuilder WithLifetime(ResourceLifetime lifetime);
}

public interface IContainerResourceBuilder :
    ILifetimeBoundResourceBuilder<IContainerResourceBuilder>
{
    IContainerResourceBuilder WithImage(string image);

    IContainerResourceBuilder WithContainerEngine(string containerEngineId);

    IContainerResourceBuilder WithContainerEngine(ICloudShellResourceBuilder containerEngine);

    IContainerResourceBuilder WithEndpoint(
        string name,
        int targetPort,
        int? port = null,
        string protocol = "tcp",
        ResourceExposureScope exposure = ResourceExposureScope.Local);

    IContainerResourceBuilder WithEnvironment(
        IReadOnlyList<EnvironmentVariableAssignment> environmentVariables);

    IContainerResourceBuilder WithEnvironment(
        string name,
        string value);

    IContainerResourceBuilder WithReplicas(int replicas);

    new IContainerResourceBuilder DependsOn(string resourceId);

    new IContainerResourceBuilder DependsOn(ICloudShellResourceBuilder resource);

    new IContainerResourceBuilder DependsOn(IEnumerable<string> resourceIds);

    new IContainerResourceBuilder DependsOn(IEnumerable<ICloudShellResourceBuilder> resources);

    new IContainerResourceBuilder WithResourceGroup(string? resourceGroupId);

    new IContainerResourceBuilder WithParent(string? parentResourceId);

    new IContainerResourceBuilder WithParent(ICloudShellResourceBuilder resource);

    new IContainerResourceBuilder WithReference(string resourceId);

    new IContainerResourceBuilder WithReference(ICloudShellResourceBuilder resource);

    new IContainerResourceBuilder WithReferences(IEnumerable<string> resourceIds);

    new IContainerResourceBuilder Persist(bool overwrite = false);
}
