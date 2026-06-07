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
