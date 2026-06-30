using System.Diagnostics;
using System.Text;

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

        var arguments = new StringBuilder();
        if (GetBoolean(
                resource,
                AspNetCoreProjectResourceTypeProvider.Attributes.HotReload,
                defaultValue: true))
        {
            arguments.Append("watch --non-interactive --project ");
            arguments.Append(Quote(fullProjectPath));
            arguments.Append(" run");
            AppendLaunchSettings(arguments, resource);
        }
        else
        {
            arguments.Append("run --project ");
            arguments.Append(Quote(fullProjectPath));
            arguments.Append(" --no-build");
            AppendLaunchSettings(arguments, resource);
        }

        var projectArguments = resource.Attributes.GetString(
            AspNetCoreProjectResourceTypeProvider.Attributes.ProjectArguments);
        projectArguments = string.IsNullOrWhiteSpace(projectArguments)
            ? CreateEndpointArguments(resource)
            : projectArguments;
        if (!string.IsNullOrWhiteSpace(projectArguments))
        {
            arguments.Append(" -- ");
            arguments.Append(projectArguments);
        }

        var startInfo = new ProcessStartInfo("dotnet", arguments.ToString())
        {
            WorkingDirectory = Path.GetDirectoryName(fullProjectPath) ?? Directory.GetCurrentDirectory(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
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

    private static bool GetBoolean(
        Resource resource,
        ResourceAttributeId attributeId,
        bool defaultValue) =>
        bool.TryParse(resource.Attributes.GetString(attributeId), out var value)
            ? value
            : defaultValue;

    private static void AppendLaunchSettings(
        StringBuilder arguments,
        Resource resource)
    {
        if (!GetBoolean(
                resource,
                AspNetCoreProjectResourceTypeProvider.Attributes.UseLaunchSettings,
                defaultValue: true))
        {
            arguments.Append(" --no-launch-profile");
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
        var environmentVariables = resource.Attributes
            .GetObject<Dictionary<string, AspNetCoreProjectEnvironmentVariableValue>>(
                AspNetCoreProjectResourceTypeProvider.Attributes.EnvironmentVariables);
        if (environmentVariables is null)
        {
            return;
        }

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
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            startInfo.Environment[name.Trim()] = value;
        }
    }

    private static string Quote(string value) =>
        "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
