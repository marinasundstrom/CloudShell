namespace CloudShell.Abstractions.ResourceManager;

public enum ResourceWorkloadKind
{
    LocalExecutable,
    ContainerImage,
    ContainerBuild
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
    int Replicas = 1,
    IReadOnlyList<EnvironmentVariableAssignment>? EnvironmentVariables = null)
{
    public IReadOnlyList<EnvironmentVariableAssignment> WorkloadEnvironmentVariables =>
        EnvironmentVariables ?? [];
}

public interface IContainerResourceBuilder : ICloudShellResourceBuilder
{
    IContainerResourceBuilder WithImage(string image);

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
