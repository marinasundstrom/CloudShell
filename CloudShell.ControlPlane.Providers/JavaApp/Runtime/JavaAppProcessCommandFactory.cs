using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CloudShell.ControlPlane.Providers;

public sealed class JavaAppProcessCommandFactory
{
    private const string MavenDefaultArguments = "package";
    private const string GradleDefaultArguments = "build";

    public ProcessStartInfo CreateStartInfo(
        Resource resource,
        string fullProjectPath,
        IReadOnlyDictionary<string, string>? derivedEnvironmentVariables = null)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullProjectPath);

        var command = resource.Attributes.GetString(
            JavaAppResourceTypeProvider.Attributes.Command);
        command = string.IsNullOrWhiteSpace(command)
            ? "java"
            : command.Trim();

        var startInfo = new ProcessStartInfo(command)
        {
            WorkingDirectory = fullProjectPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in SplitArguments(resource.Attributes.GetString(
            JavaAppResourceTypeProvider.Attributes.JvmArguments)))
        {
            startInfo.ArgumentList.Add(argument);
        }

        var mainClass = resource.Attributes.GetString(
            JavaAppResourceTypeProvider.Attributes.MainClass);
        if (!string.IsNullOrWhiteSpace(mainClass))
        {
            var classPath = resource.Attributes.GetString(
                JavaAppResourceTypeProvider.Attributes.ClassPath);
            if (!string.IsNullOrWhiteSpace(classPath))
            {
                startInfo.ArgumentList.Add("-cp");
                startInfo.ArgumentList.Add(classPath.Trim());
            }

            startInfo.ArgumentList.Add(mainClass.Trim());
        }
        else
        {
            var artifactPath = resource.Attributes.GetString(
                JavaAppResourceTypeProvider.Attributes.ArtifactPath);
            if (!string.IsNullOrWhiteSpace(artifactPath))
            {
                startInfo.ArgumentList.Add("-jar");
                startInfo.ArgumentList.Add(artifactPath.Trim());
            }
        }

        foreach (var argument in SplitArguments(resource.Attributes.GetString(
            JavaAppResourceTypeProvider.Attributes.Arguments)))
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment[JavaAppEnvironmentNames.ResourceId] =
            resource.EffectiveResourceId;
        startInfo.Environment[JavaAppEnvironmentNames.ResourceName] =
            resource.Name;
        ApplyEndpointEnvironmentVariables(resource, startInfo);
        ApplyDerivedEnvironmentVariables(derivedEnvironmentVariables, startInfo);
        ApplyEnvironmentVariables(resource, startInfo);

        return startInfo;
    }

    public ProcessStartInfo? CreateBuildStartInfo(
        Resource resource,
        string fullProjectPath)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullProjectPath);

        var buildTool = resource.Attributes.GetString(
            JavaAppResourceTypeProvider.Attributes.BuildTool);
        if (string.IsNullOrWhiteSpace(buildTool))
        {
            return null;
        }

        var trimmedBuildTool = buildTool.Trim();
        if (!JavaAppBuildTools.IsSupported(trimmedBuildTool))
        {
            return null;
        }

        var startInfo = new ProcessStartInfo(
            ResolveBuildCommand(trimmedBuildTool, fullProjectPath))
        {
            WorkingDirectory = fullProjectPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var arguments = resource.Attributes.GetString(
            JavaAppResourceTypeProvider.Attributes.BuildArguments);
        arguments = string.IsNullOrWhiteSpace(arguments)
            ? DefaultBuildArguments(trimmedBuildTool)
            : arguments;
        foreach (var argument in SplitArguments(arguments))
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static void ApplyEndpointEnvironmentVariables(
        Resource resource,
        ProcessStartInfo startInfo)
    {
        var endpoints = resource.Attributes
            .GetObject<NetworkingEndpointRequestValue[]>(
                JavaAppResourceTypeProvider.Attributes.EndpointRequests);
        var firstHttpEndpoint = endpoints?
            .FirstOrDefault(endpoint =>
                endpoint.Port is > 0 &&
                endpoint.Protocol.Equals("http", StringComparison.OrdinalIgnoreCase));

        if (firstHttpEndpoint?.Port is { } port &&
            !startInfo.Environment.ContainsKey("PORT"))
        {
            startInfo.Environment["PORT"] = port.ToString();
        }
    }

    private static void ApplyEnvironmentVariables(
        Resource resource,
        ProcessStartInfo startInfo)
    {
        var environmentVariables = ProjectEnvironmentVariableReader.ReadJavaApp(resource.Attributes);

        foreach (var (name, variable) in environmentVariables)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (variable.ConfigurationEntryRef is not null ||
                variable.SecretRef is not null)
            {
                continue;
            }

            startInfo.Environment[name.Trim()] = variable.Value ?? string.Empty;
        }
    }

    private static void ApplyDerivedEnvironmentVariables(
        IReadOnlyDictionary<string, string>? environmentVariables,
        ProcessStartInfo startInfo)
    {
        if (environmentVariables is null)
        {
            return;
        }

        foreach (var (name, value) in environmentVariables)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                startInfo.Environment[name.Trim()] = value;
            }
        }
    }

    private static IEnumerable<string> SplitArguments(string? arguments) =>
        string.IsNullOrWhiteSpace(arguments)
            ? []
            : arguments
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string ResolveBuildCommand(
        string buildTool,
        string fullProjectPath)
    {
        if (string.Equals(buildTool, JavaAppBuildTools.Maven, StringComparison.OrdinalIgnoreCase))
        {
            return ResolveWrapperOrCommand(
                fullProjectPath,
                windowsWrapper: "mvnw.cmd",
                unixWrapper: "mvnw",
                command: "mvn");
        }

        return ResolveWrapperOrCommand(
            fullProjectPath,
            windowsWrapper: "gradlew.bat",
            unixWrapper: "gradlew",
            command: "gradle");
    }

    private static string ResolveWrapperOrCommand(
        string fullProjectPath,
        string windowsWrapper,
        string unixWrapper,
        string command)
    {
        var wrapper = Path.Combine(
            fullProjectPath,
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? windowsWrapper
                : unixWrapper);

        return File.Exists(wrapper)
            ? wrapper
            : command;
    }

    private static string DefaultBuildArguments(string buildTool) =>
        string.Equals(buildTool, JavaAppBuildTools.Maven, StringComparison.OrdinalIgnoreCase)
            ? MavenDefaultArguments
            : GradleDefaultArguments;
}

public static class JavaAppEnvironmentNames
{
    public const string ResourceId = "CLOUDSHELL_RESOURCE_ID";
    public const string ResourceName = "CLOUDSHELL_RESOURCE_NAME";
}
