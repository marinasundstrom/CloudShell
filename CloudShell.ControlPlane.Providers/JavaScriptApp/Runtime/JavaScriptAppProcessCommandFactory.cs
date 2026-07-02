using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CloudShell.ControlPlane.Providers;

public sealed class JavaScriptAppProcessCommandFactory
{
    public ProcessStartInfo CreateStartInfo(
        Resource resource,
        string fullProjectPath,
        IReadOnlyDictionary<string, string>? derivedEnvironmentVariables = null)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullProjectPath);

        var packageManager = resource.Attributes.GetString(
            JavaScriptAppResourceTypeProvider.Attributes.PackageManager);
        packageManager = string.IsNullOrWhiteSpace(packageManager)
            ? "npm"
            : packageManager.Trim();

        var script = resource.Attributes.GetString(
            JavaScriptAppResourceTypeProvider.Attributes.Script);
        script = string.IsNullOrWhiteSpace(script)
            ? "dev"
            : script.Trim();

        var startInfo = new ProcessStartInfo(GetExecutableName(packageManager))
        {
            WorkingDirectory = fullProjectPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add(script);

        var arguments = resource.Attributes.GetString(
            JavaScriptAppResourceTypeProvider.Attributes.Arguments);
        if (!string.IsNullOrWhiteSpace(arguments))
        {
            startInfo.ArgumentList.Add("--");
            foreach (var argument in SplitArguments(arguments))
            {
                startInfo.ArgumentList.Add(argument);
            }
        }

        startInfo.Environment[JavaScriptAppEnvironmentNames.ResourceId] =
            resource.EffectiveResourceId;
        startInfo.Environment[JavaScriptAppEnvironmentNames.ResourceName] =
            resource.Name;
        ApplyEndpointEnvironmentVariables(resource, startInfo);
        ApplyDerivedEnvironmentVariables(derivedEnvironmentVariables, startInfo);
        ApplyEnvironmentVariables(resource, startInfo);

        return startInfo;
    }

    private static string GetExecutableName(string packageManager) =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
        packageManager.Equals("npm", StringComparison.OrdinalIgnoreCase)
            ? "npm.cmd"
            : packageManager;

    private static void ApplyEndpointEnvironmentVariables(
        Resource resource,
        ProcessStartInfo startInfo)
    {
        var endpoints = resource.Attributes
            .GetObject<NetworkingEndpointRequestValue[]>(
                JavaScriptAppResourceTypeProvider.Attributes.EndpointRequests);
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
        var environmentVariables = resource.Attributes
            .GetObject<Dictionary<string, JavaScriptAppEnvironmentVariableValue>>(
                JavaScriptAppResourceTypeProvider.Attributes.EnvironmentVariables);
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
            if (!string.IsNullOrWhiteSpace(name))
            {
                startInfo.Environment[name.Trim()] = value;
            }
        }
    }

    private static IEnumerable<string> SplitArguments(string arguments) =>
        arguments
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public static class JavaScriptAppEnvironmentNames
{
    public const string ResourceId = "CLOUDSHELL_RESOURCE_ID";
    public const string ResourceName = "CLOUDSHELL_RESOURCE_NAME";
}
