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
            ProjectEnvironmentVariableReader.ReadJavaScriptApp(resource.Attributes));
        Add(
            environment,
            ProjectEnvironmentVariableReader.ReadJavaApp(resource.Attributes));
        Add(
            environment,
            ProjectEnvironmentVariableReader.ReadAspNetCoreProject(resource.Attributes));
        Add(
            environment,
            ProjectEnvironmentVariableReader.ReadPythonApp(resource.Attributes));
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
        IReadOnlyDictionary<string, JavaAppEnvironmentVariableValue>? values)
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

    private static void Add(
        List<EnvironmentVariableAssignment> environment,
        IReadOnlyDictionary<string, PythonAppEnvironmentVariableValue>? values)
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
