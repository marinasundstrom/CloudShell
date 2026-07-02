using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.Providers;

internal static class ContainerizedProjectEnvironmentVariables
{
    public static IReadOnlyList<EnvironmentVariableAssignment> Read(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        var environment = new List<EnvironmentVariableAssignment>();
        Add(
            environment,
            resource.Attributes.GetObject<Dictionary<string, JavaScriptAppEnvironmentVariableValue>>(
                JavaScriptAppResourceTypeProvider.Attributes.EnvironmentVariables));
        Add(
            environment,
            resource.Attributes.GetObject<Dictionary<string, AspNetCoreProjectEnvironmentVariableValue>>(
                AspNetCoreProjectResourceTypeProvider.Attributes.EnvironmentVariables));
        return environment;
    }

    private static void Add(
        List<EnvironmentVariableAssignment> environment,
        IReadOnlyDictionary<string, JavaScriptAppEnvironmentVariableValue>? values)
    {
        if (values is null)
        {
            return;
        }

        foreach (var (name, value) in values)
        {
            AddLiteral(environment, name, value.Value);
        }
    }

    private static void Add(
        List<EnvironmentVariableAssignment> environment,
        IReadOnlyDictionary<string, AspNetCoreProjectEnvironmentVariableValue>? values)
    {
        if (values is null)
        {
            return;
        }

        foreach (var (name, value) in values)
        {
            AddLiteral(environment, name, value.Value);
        }
    }

    private static void AddLiteral(
        List<EnvironmentVariableAssignment> environment,
        string name,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            value is null)
        {
            return;
        }

        environment.Add(new EnvironmentVariableAssignment(name.Trim(), value));
    }
}
