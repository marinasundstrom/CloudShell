using CloudShell.Abstractions.ResourceManager;
using System.Globalization;
using System.Text.Json;

namespace CloudShell.Providers.Applications;

public sealed partial class ApplicationResourceService
{
    private IReadOnlyList<ServicePort> ResolveEndpointPorts(
        IReadOnlyList<ServicePort> ports,
        string resourceType,
        string? endpoint,
        string? projectPath,
        bool useLaunchSettingsEndpoints)
    {
        var normalized = NormalizeEndpointPorts(ports);
        if (normalized.Count > 0 ||
            !string.Equals(resourceType, ApplicationResourceTypes.AspNetCoreProject, StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        if (useLaunchSettingsEndpoints)
        {
            var launchSettingsPorts = TryReadLaunchSettingsEndpointPorts(projectPath);
            if (launchSettingsPorts.Count > 0)
            {
                return launchSettingsPorts;
            }
        }

        return ApplicationProviderServiceCollectionExtensions.CreateAspNetCoreProjectEndpointPorts(endpoint);
    }

    private IReadOnlyList<ServicePort> TryReadLaunchSettingsEndpointPorts(string? projectPath)
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

    private int ResolveLocalPort(string resourceId, ServicePort port)
    {
        if (port.Port is not null)
        {
            return Math.Max(1, port.Port.Value);
        }

        var start = Math.Max(1, options.AutoLocalPortStart);
        var end = Math.Max(start, options.AutoLocalPortEnd);
        var range = end - start + 1;
        return start + (int)(StableHash($"{resourceId}:{port.Name}") % (uint)range);
    }

    private int ResolveReplicaProbeLocalPort(
        string resourceId,
        ServicePort port,
        int replicaOrdinal)
    {
        var start = Math.Max(1, options.AutoLocalPortStart);
        var end = Math.Max(start, options.AutoLocalPortEnd);
        var range = end - start + 1;
        var normalizedReplicaOrdinal = Math.Max(1, replicaOrdinal);
        return start + (int)(StableHash(
            $"{resourceId}:replica:{normalizedReplicaOrdinal.ToString(CultureInfo.InvariantCulture)}:{port.Name}") %
            (uint)range);
    }

    private static IReadOnlyList<ServicePort> NormalizeEndpointPorts(
        IReadOnlyList<ServicePort> ports,
        string resourceType,
        string? endpoint = null)
    {
        var normalized = ports
            .Where(port => !string.IsNullOrWhiteSpace(port.Name))
            .Select(port => port with
            {
                Name = port.Name.Trim(),
                Protocol = NormalizeProtocol(port.Protocol),
                TargetPort = Math.Max(1, port.TargetPort),
                Port = port.Port is null ? null : Math.Max(1, port.Port.Value),
                NetworkResourceId = NormalizeNullable(port.NetworkResourceId),
                Host = NormalizeNullable(port.Host),
                IPAddress = NormalizeNullable(port.IPAddress),
                ProviderEndpointId = NormalizeNullable(port.ProviderEndpointId)
            })
            .DistinctBy(port => port.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return normalized.Length == 0 &&
            string.Equals(resourceType, ApplicationResourceTypes.AspNetCoreProject, StringComparison.OrdinalIgnoreCase)
            ? ApplicationProviderServiceCollectionExtensions.CreateAspNetCoreProjectEndpointPorts(endpoint)
            : normalized;
    }

    private static IReadOnlyList<ServicePort> NormalizeEndpointPorts(
        IReadOnlyList<ServicePort> ports) =>
        NormalizeEndpointPorts(ports, ApplicationResourceTypes.ExecutableApplication);

    private static string NormalizeProtocol(string? protocol) =>
        string.IsNullOrWhiteSpace(protocol) ? "tcp" : protocol.Trim().ToLowerInvariant();

    private static string NormalizeContainerPublishProtocol(string? protocol) =>
        NormalizeProtocol(protocol) switch
        {
            "http" or "https" => "tcp",
            "udp" => "udp",
            "sctp" => "sctp",
            _ => "tcp"
        };
}
