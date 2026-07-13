using CloudShell.ControlPlane.Providers;
using ResourceGraphState = CloudShell.ResourceModel.ResourceState;

namespace CloudShell.ResourceModel.Tests;

public sealed class PythonAppProcessCommandFactoryTests
{
    [Fact]
    public void CreateStartInfo_UsesPlatformDefaultCommandForScript()
    {
        var resource = CreateResource(scriptPath: "app.py");
        var projectPath = CreateFullPath("repo", "python api");

        var startInfo = new PythonAppProcessCommandFactory(
                new PythonAppProcessCommandPlatform("python3"))
            .CreateStartInfo(resource, projectPath);

        Assert.Equal("python3", startInfo.FileName);
        Assert.Equal(["app.py"], startInfo.ArgumentList.ToArray());
        Assert.Equal(projectPath, startInfo.WorkingDirectory);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
    }

    [Fact]
    public void CreateStartInfo_AllowsExplicitWindowsPythonLauncherCommand()
    {
        var resource = CreateResource(command: "py", module: "sample.api", arguments: "--port 5188");

        var startInfo = new PythonAppProcessCommandFactory(
                new PythonAppProcessCommandPlatform("python3"))
            .CreateStartInfo(resource, CreateFullPath("repo", "api"));

        Assert.Equal("py", startInfo.FileName);
        Assert.Equal(["-m", "sample.api", "--port", "5188"], startInfo.ArgumentList.ToArray());
    }

    [Fact]
    public void CreateStartInfo_PreservesScriptPathWithSpacesAsSingleArgument()
    {
        var resource = CreateResource(scriptPath: "src/my app.py", arguments: "--mode dev");

        var startInfo = new PythonAppProcessCommandFactory(
                new PythonAppProcessCommandPlatform("python3"))
            .CreateStartInfo(resource, CreateFullPath("repo", "api"));

        Assert.Equal(["src/my app.py", "--mode", "dev"], startInfo.ArgumentList.ToArray());
    }

    [Fact]
    public void CreateStartInfo_LetsExplicitPortEnvironmentOverrideEndpointPort()
    {
        var resource = CreateResource(endpointPort: 5188, environmentPort: "9000");

        var startInfo = new PythonAppProcessCommandFactory(
                new PythonAppProcessCommandPlatform("python3"))
            .CreateStartInfo(resource, CreateFullPath("repo", "api"));

        Assert.Equal("9000", startInfo.Environment["PORT"]);
        Assert.Equal("application.python-app:api", startInfo.Environment[PythonAppEnvironmentNames.ResourceId]);
        Assert.Equal("api", startInfo.Environment[PythonAppEnvironmentNames.ResourceName]);
    }

    private static Resource CreateResource(
        string? command = null,
        string? scriptPath = null,
        string? module = null,
        string? arguments = null,
        int? endpointPort = null,
        string? environmentPort = null)
    {
        var attributes = new Dictionary<ResourceAttributeId, ResourceAttributeValue>
        {
            [PythonAppResourceTypeProvider.Attributes.ProjectPath] = "src/api"
        };

        if (command is not null)
        {
            attributes[PythonAppResourceTypeProvider.Attributes.Command] = command;
        }

        if (scriptPath is not null)
        {
            attributes[PythonAppResourceTypeProvider.Attributes.ScriptPath] = scriptPath;
        }

        if (module is not null)
        {
            attributes[PythonAppResourceTypeProvider.Attributes.Module] = module;
        }

        if (arguments is not null)
        {
            attributes[PythonAppResourceTypeProvider.Attributes.Arguments] = arguments;
        }

        if (endpointPort is not null)
        {
            attributes[PythonAppResourceTypeProvider.Attributes.EndpointRequests] =
                ResourceAttributeValue.FromObject(new[]
                {
                    new NetworkingEndpointRequestValue(
                        "http",
                        "http",
                        Host: "127.0.0.1",
                        Port: endpointPort,
                        Exposure: "Local")
                });
        }

        if (environmentPort is not null)
        {
            attributes[PythonAppResourceTypeProvider.Attributes.EnvironmentVariables] =
                ResourceAttributeValue.FromObject(
                    new Dictionary<string, PythonAppEnvironmentVariableValue>
                    {
                        ["PORT"] = new(environmentPort)
                    });
        }

        var resolver = new ResourceResolver(
            [PythonAppResourceTypeProvider.ClassDefinition],
            [new PythonAppResourceTypeProvider().TypeDefinition]);

        return resolver.Resolve(new ResourceGraphState(
            "api",
            PythonAppResourceTypeProvider.ResourceTypeId,
            ProviderId: PythonAppResourceTypeProvider.ProviderId,
            Attributes: attributes));
    }

    private static string CreateFullPath(params string[] paths) =>
        Path.GetFullPath(Path.Combine(PrependTempPath(paths)));

    private static string[] PrependTempPath(string[] paths)
    {
        var segments = new string[paths.Length + 1];
        segments[0] = Path.GetTempPath();
        Array.Copy(paths, 0, segments, 1, paths.Length);
        return segments;
    }
}
