namespace CloudShell.Abstractions.ResourceManager;

public interface IResourceEnvironmentVariableProvider
{
    IReadOnlyList<EnvironmentVariableAssignment> GetEnvironmentVariables(string resourceId);
}
