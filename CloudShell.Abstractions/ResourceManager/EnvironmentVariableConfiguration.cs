namespace CloudShell.Abstractions.ResourceManager;

public interface IEnvironmentVariableConfiguration
{
    IReadOnlyList<EnvironmentVariableAssignment> EnvironmentVariables { get; }
}

public sealed record EnvironmentVariableAssignment(
    string Name,
    string Value);
