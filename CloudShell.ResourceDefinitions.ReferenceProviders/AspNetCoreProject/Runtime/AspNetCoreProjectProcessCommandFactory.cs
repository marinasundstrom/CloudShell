using System.Diagnostics;
using System.Text;

namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class AspNetCoreProjectProcessCommandFactory
{
    public ProcessStartInfo CreateStartInfo(
        Resource resource,
        string fullProjectPath)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullProjectPath);

        var arguments = new StringBuilder();
        if (GetBoolean(
                resource,
                AspNetCoreProjectResourceTypeProvider.Attributes.HotReload,
                defaultValue: true))
        {
            arguments.Append("watch --project ");
            arguments.Append(Quote(fullProjectPath));
            arguments.Append(" run");
            AppendLaunchSettings(arguments, resource);
        }
        else
        {
            arguments.Append("run --project ");
            arguments.Append(Quote(fullProjectPath));
            AppendLaunchSettings(arguments, resource);
        }

        var projectArguments = resource.Attributes.GetString(
            AspNetCoreProjectResourceTypeProvider.Attributes.ProjectArguments);
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

    private static string Quote(string value) =>
        "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
