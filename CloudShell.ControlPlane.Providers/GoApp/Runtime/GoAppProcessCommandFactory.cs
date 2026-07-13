using System.Diagnostics;

namespace CloudShell.ControlPlane.Providers;

public sealed class GoAppProcessCommandFactory
{
    private readonly GoAppProcessCommandPlatform _platform;

    public GoAppProcessCommandFactory()
        : this(GoAppProcessCommandPlatform.Current)
    {
    }

    internal GoAppProcessCommandFactory(GoAppProcessCommandPlatform platform)
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

        var binaryPath = resource.Attributes.GetString(
            GoAppResourceTypeProvider.Attributes.BinaryPath);
        var startInfo = string.IsNullOrWhiteSpace(binaryPath)
            ? CreateGoRunStartInfo(resource, fullProjectPath)
            : CreateBinaryStartInfo(resource, fullProjectPath, binaryPath);

        startInfo.Environment[GoAppEnvironmentNames.ResourceId] =
            resource.EffectiveResourceId;
        startInfo.Environment[GoAppEnvironmentNames.ResourceName] =
            resource.Name;
        ApplyEndpointEnvironmentVariables(resource, startInfo);
        ApplyDerivedEnvironmentVariables(derivedEnvironmentVariables, startInfo);
        ApplyEnvironmentVariables(resource, startInfo);

        return startInfo;
    }

    private static ProcessStartInfo CreateGoRunStartInfo(
        Resource resource,
        string fullProjectPath)
    {
        var command = resource.Attributes.GetString(
            GoAppResourceTypeProvider.Attributes.Command);
        command = string.IsNullOrWhiteSpace(command)
            ? "go"
            : command.Trim();

        var packagePath = resource.Attributes.GetString(
            GoAppResourceTypeProvider.Attributes.PackagePath);
        packagePath = string.IsNullOrWhiteSpace(packagePath)
            ? "."
            : packagePath.Trim();

        var startInfo = CreateBaseStartInfo(command, fullProjectPath);
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add(packagePath);
        AddApplicationArguments(resource, startInfo);

        return startInfo;
    }

    private ProcessStartInfo CreateBinaryStartInfo(
        Resource resource,
        string fullProjectPath,
        string binaryPath)
    {
        var effectiveBinaryPath = _platform.ResolveBinaryPath(binaryPath, fullProjectPath);
        var startInfo = CreateBaseStartInfo(effectiveBinaryPath, fullProjectPath);
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
            GoAppResourceTypeProvider.Attributes.Arguments)))
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
                GoAppResourceTypeProvider.Attributes.EndpointRequests);
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
        var environmentVariables = ProjectEnvironmentVariableReader.ReadGoApp(resource.Attributes);

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

internal sealed record GoAppProcessCommandPlatform(bool IsWindows)
{
    public static GoAppProcessCommandPlatform Current => new(OperatingSystem.IsWindows());

    public string ResolveBinaryPath(string binaryPath, string fullProjectPath)
    {
        var trimmed = binaryPath.Trim();
        return IsRootedPath(trimmed)
            ? trimmed
            : Path.GetFullPath(trimmed, fullProjectPath);
    }

    private bool IsRootedPath(string path) =>
        IsWindows
            ? IsWindowsRootedPath(path) || IsWindowsUncPath(path)
            : path.StartsWith('/', StringComparison.Ordinal);

    private static bool IsWindowsRootedPath(string path) =>
        path.Length >= 3 &&
        char.IsAsciiLetter(path[0]) &&
        path[1] == ':' &&
        (path[2] == '\\' || path[2] == '/');

    private static bool IsWindowsUncPath(string path) =>
        path.StartsWith(@"\\", StringComparison.Ordinal) ||
        path.StartsWith("//", StringComparison.Ordinal);
}

public static class GoAppEnvironmentNames
{
    public const string ResourceId = "CLOUDSHELL_RESOURCE_ID";
    public const string ResourceName = "CLOUDSHELL_RESOURCE_NAME";
}
