using System.Diagnostics;

namespace CloudShell.ControlPlane.Providers;

public sealed class PythonAppProcessCommandFactory
{
    private readonly PythonAppProcessCommandPlatform _platform;

    public PythonAppProcessCommandFactory()
        : this(PythonAppProcessCommandPlatform.Current)
    {
    }

    internal PythonAppProcessCommandFactory(PythonAppProcessCommandPlatform platform)
    {
        _platform = platform;
    }

    public ProcessStartInfo CreateStartInfo(
        Resource resource,
        string fullProjectPath,
        IReadOnlyDictionary<string, string>? derivedEnvironmentVariables = null)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullProjectPath);

        var startInfo = CreatePythonStartInfo(resource, fullProjectPath);

        startInfo.Environment[PythonAppEnvironmentNames.ResourceId] =
            resource.EffectiveResourceId;
        startInfo.Environment[PythonAppEnvironmentNames.ResourceName] =
            resource.Name;
        ApplyEndpointEnvironmentVariables(resource, startInfo);
        ApplyDerivedEnvironmentVariables(derivedEnvironmentVariables, startInfo);
        ApplyEnvironmentVariables(resource, startInfo);

        return startInfo;
    }

    private ProcessStartInfo CreatePythonStartInfo(
        Resource resource,
        string fullProjectPath)
    {
        var command = resource.Attributes.GetString(
            PythonAppResourceTypeProvider.Attributes.Command);
        command = string.IsNullOrWhiteSpace(command)
            ? _platform.DefaultCommand
            : command.Trim();

        var startInfo = CreateBaseStartInfo(command, fullProjectPath);
        var module = resource.Attributes.GetString(
            PythonAppResourceTypeProvider.Attributes.Module);
        if (!string.IsNullOrWhiteSpace(module))
        {
            startInfo.ArgumentList.Add("-m");
            startInfo.ArgumentList.Add(module.Trim());
        }
        else
        {
            var scriptPath = resource.Attributes.GetString(
                PythonAppResourceTypeProvider.Attributes.ScriptPath);
            scriptPath = string.IsNullOrWhiteSpace(scriptPath)
                ? "app.py"
                : scriptPath.Trim();
            startInfo.ArgumentList.Add(scriptPath);
        }

        AddApplicationArguments(resource, startInfo);

        return startInfo;
    }

    private static ProcessStartInfo CreateBaseStartInfo(
        string command,
        string workingDirectory) =>
        new(command)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

    private static void AddApplicationArguments(
        Resource resource,
        ProcessStartInfo startInfo)
    {
        foreach (var argument in SplitArguments(resource.Attributes.GetString(
            PythonAppResourceTypeProvider.Attributes.Arguments)))
        {
            startInfo.ArgumentList.Add(argument);
        }
    }

    private static void ApplyEndpointEnvironmentVariables(
        Resource resource,
        ProcessStartInfo startInfo)
    {
        var endpoints = resource.Attributes
            .GetObject<NetworkingEndpointRequestValue[]>(
                PythonAppResourceTypeProvider.Attributes.EndpointRequests);
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
        var environmentVariables = ProjectEnvironmentVariableReader.ReadPythonApp(resource.Attributes);

        foreach (var (name, variable) in environmentVariables)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (variable.ConfigurationSettingRef is not null ||
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
}

internal sealed record PythonAppProcessCommandPlatform(string DefaultCommand)
{
    public static PythonAppProcessCommandPlatform Current => new("python3");
}

public static class PythonAppEnvironmentNames
{
    public const string ResourceId = "CLOUDSHELL_RESOURCE_ID";
    public const string ResourceName = "CLOUDSHELL_RESOURCE_NAME";
}
