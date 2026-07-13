using System.Diagnostics;

namespace CloudShell.ControlPlane.Providers;

public sealed class AspNetCoreProjectProcessCommandFactory
{
    public ProcessStartInfo CreateStartInfo(
        Resource resource,
        string fullProjectPath,
        IReadOnlyDictionary<string, string>? derivedEnvironmentVariables = null)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullProjectPath);

        var startInfo = CreateBaseStartInfo(
            "dotnet",
            Path.GetDirectoryName(fullProjectPath) ?? Directory.GetCurrentDirectory());
        if (GetBoolean(
                resource,
                AspNetCoreProjectResourceTypeProvider.Attributes.HotReload,
                defaultValue: true))
        {
            startInfo.ArgumentList.Add("watch");
            startInfo.ArgumentList.Add("--non-interactive");
            startInfo.ArgumentList.Add("--project");
            startInfo.ArgumentList.Add(fullProjectPath);
            startInfo.ArgumentList.Add("run");
            AppendLaunchSettings(startInfo, resource);
        }
        else
        {
            startInfo.ArgumentList.Add("run");
            startInfo.ArgumentList.Add("--project");
            startInfo.ArgumentList.Add(fullProjectPath);
            startInfo.ArgumentList.Add("--no-build");
            AppendLaunchSettings(startInfo, resource);
        }

        var projectArguments = resource.Attributes.GetString(
            AspNetCoreProjectResourceTypeProvider.Attributes.ProjectArguments);
        projectArguments = string.IsNullOrWhiteSpace(projectArguments)
            ? CreateEndpointArguments(resource)
            : projectArguments;
        if (!string.IsNullOrWhiteSpace(projectArguments))
        {
            startInfo.ArgumentList.Add("--");
            AddArguments(startInfo, projectArguments);
        }

        startInfo.Environment[AspNetCoreProjectEnvironmentNames.ResourceId] =
            resource.EffectiveResourceId;
        startInfo.Environment[AspNetCoreProjectEnvironmentNames.ResourceName] =
            resource.Name;
        if (GetBoolean(
                resource,
                AspNetCoreProjectResourceTypeProvider.Attributes.HotReload,
                defaultValue: true))
        {
            startInfo.Environment[AspNetCoreProjectEnvironmentNames.DotNetWatchRestartOnRudeEdit] =
                "true";
        }

        ApplyDerivedEnvironmentVariables(derivedEnvironmentVariables, startInfo);
        ApplyEnvironmentVariables(resource, startInfo);

        return startInfo;
    }

    public ProcessStartInfo CreatePublishedOutputStartInfo(
        Resource resource,
        string applicationAssemblyPath,
        IReadOnlyDictionary<string, string>? derivedEnvironmentVariables = null)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationAssemblyPath);

        var startInfo = CreateBaseStartInfo(
            "dotnet",
            Path.GetDirectoryName(applicationAssemblyPath) ?? Directory.GetCurrentDirectory());
        startInfo.ArgumentList.Add(applicationAssemblyPath);

        var projectArguments = resource.Attributes.GetString(
            AspNetCoreProjectResourceTypeProvider.Attributes.ProjectArguments);
        projectArguments = string.IsNullOrWhiteSpace(projectArguments)
            ? CreateEndpointArguments(resource)
            : projectArguments;
        if (!string.IsNullOrWhiteSpace(projectArguments))
        {
            AddArguments(startInfo, projectArguments);
        }

        startInfo.Environment[AspNetCoreProjectEnvironmentNames.ResourceId] =
            resource.EffectiveResourceId;
        startInfo.Environment[AspNetCoreProjectEnvironmentNames.ResourceName] =
            resource.Name;

        ApplyDerivedEnvironmentVariables(derivedEnvironmentVariables, startInfo);
        ApplyEnvironmentVariables(resource, startInfo);

        return startInfo;
    }

    public ProcessStartInfo CreateExecutableStartInfo(
        Resource resource,
        string executablePath,
        IReadOnlyDictionary<string, string>? derivedEnvironmentVariables = null) =>
        CreatePublishedOutputStartInfo(resource, executablePath, derivedEnvironmentVariables);

    private static bool GetBoolean(
        Resource resource,
        ResourceAttributeId attributeId,
        bool defaultValue) =>
        bool.TryParse(resource.Attributes.GetString(attributeId), out var value)
            ? value
            : defaultValue;

    private static ProcessStartInfo CreateBaseStartInfo(
        string fileName,
        string workingDirectory) =>
        new(fileName)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

    private static void AppendLaunchSettings(
        ProcessStartInfo startInfo,
        Resource resource)
    {
        if (!GetBoolean(
                resource,
                AspNetCoreProjectResourceTypeProvider.Attributes.UseLaunchSettings,
                defaultValue: true))
        {
            startInfo.ArgumentList.Add("--no-launch-profile");
        }
    }

    private static string? CreateEndpointArguments(Resource resource)
    {
        var urls = resource.Attributes
            .GetObject<NetworkingEndpointRequestValue[]>(
                AspNetCoreProjectResourceTypeProvider.Attributes.EndpointRequests)?
            .Select(CreateEndpointUrl)
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .ToArray();

        return urls is { Length: > 0 }
            ? "--urls " + string.Join(';', urls)
            : null;
    }

    private static string? CreateEndpointUrl(NetworkingEndpointRequestValue endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint.Protocol) ||
            endpoint.Port is null)
        {
            return null;
        }

        var host = !string.IsNullOrWhiteSpace(endpoint.Host)
            ? endpoint.Host
            : endpoint.IpAddress;
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        return $"{endpoint.Protocol}://{host}:{endpoint.Port.Value}";
    }

    private static void ApplyEnvironmentVariables(
        Resource resource,
        ProcessStartInfo startInfo)
    {
        var environmentVariables = ProjectEnvironmentVariableReader.ReadAspNetCoreProject(resource.Attributes);

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
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            startInfo.Environment[name.Trim()] = value;
        }
    }

    private static void AddArguments(ProcessStartInfo startInfo, string arguments)
    {
        foreach (var argument in arguments
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            startInfo.ArgumentList.Add(argument);
        }
    }
}
