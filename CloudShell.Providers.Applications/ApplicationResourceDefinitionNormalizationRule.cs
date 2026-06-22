using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.Hosting;
using System.Globalization;
using System.Text.Json;

namespace CloudShell.Providers.Applications;

public interface IApplicationResourceDefinitionNormalizationRule
{
    bool AppliesTo(ApplicationResourceDefinition definition);

    ApplicationResourceDefinition Normalize(
        ApplicationResourceDefinition definition,
        ApplicationResourceDefinitionNormalizationContext context);

    ApplicationResourceDefinition Resolve(
        ApplicationResourceDefinition definition,
        ApplicationResourceDefinitionNormalizationContext context) =>
        definition;
}

public sealed class ApplicationResourceDefinitionNormalizationContext(IHostEnvironment environment)
{
    public IReadOnlyList<ServicePort> TryReadLaunchSettingsEndpointPorts(string? projectPath)
    {
        var launchSettingsPath = ResolveLaunchSettingsPath(projectPath);
        if (launchSettingsPath is null ||
            !File.Exists(launchSettingsPath))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(launchSettingsPath));
            if (!document.RootElement.TryGetProperty("profiles", out var profiles) ||
                profiles.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            var profileElements = profiles
                .EnumerateObject()
                .Select(profile => profile.Value)
                .Where(profile => profile.ValueKind == JsonValueKind.Object)
                .ToArray();
            var orderedProfiles = profileElements
                .Where(IsProjectLaunchProfile)
                .Concat(profileElements.Where(profile => !IsProjectLaunchProfile(profile)));
            foreach (var profile in orderedProfiles)
            {
                if (!profile.TryGetProperty("applicationUrl", out var applicationUrl) ||
                    applicationUrl.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var endpointPorts = CreateLaunchSettingsEndpointPorts(applicationUrl.GetString());
                if (endpointPorts.Count > 0)
                {
                    return endpointPorts;
                }
            }
        }
        catch (JsonException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }

        return [];
    }

    public static IReadOnlyList<ServicePort> CreateAspNetCoreProjectEndpointPorts(string? endpoint) =>
        ApplicationProviderServiceCollectionExtensions.CreateAspNetCoreProjectEndpointPorts(endpoint);

    private string? ResolveLaunchSettingsPath(string? projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return null;
        }

        var resolvedProjectPath = Path.IsPathRooted(projectPath)
            ? projectPath
            : Path.GetFullPath(projectPath, environment.ContentRootPath);
        var projectDirectory = Directory.Exists(resolvedProjectPath)
            ? resolvedProjectPath
            : Path.GetDirectoryName(resolvedProjectPath);
        return string.IsNullOrWhiteSpace(projectDirectory)
            ? null
            : Path.Combine(projectDirectory, "Properties", "launchSettings.json");
    }

    private static bool IsProjectLaunchProfile(JsonElement profile) =>
        profile.TryGetProperty("commandName", out var commandName) &&
        commandName.ValueKind == JsonValueKind.String &&
        string.Equals(commandName.GetString(), "Project", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<ServicePort> CreateLaunchSettingsEndpointPorts(string? applicationUrl)
    {
        if (string.IsNullOrWhiteSpace(applicationUrl))
        {
            return [];
        }

        var ports = new List<ServicePort>();
        var names = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in applicationUrl.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                uri.Port <= 0)
            {
                continue;
            }

            var protocol = string.IsNullOrWhiteSpace(uri.Scheme) ? "http" : uri.Scheme;
            var name = CreateLaunchSettingsEndpointName(protocol, names);
            ports.Add(new ServicePort(name, uri.Port, uri.Port, protocol, ResourceExposureScope.Local));
        }

        return ports;
    }

    private static string CreateLaunchSettingsEndpointName(
        string protocol,
        Dictionary<string, int> names)
    {
        names.TryGetValue(protocol, out var count);
        count++;
        names[protocol] = count;
        return count == 1
            ? protocol
            : $"{protocol}-{count.ToString(CultureInfo.InvariantCulture)}";
    }
}
