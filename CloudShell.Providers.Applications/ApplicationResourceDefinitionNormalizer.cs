using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.Hosting;
using System.Globalization;
using System.Text.Json;

namespace CloudShell.Providers.Applications;

public sealed class ApplicationResourceDefinitionNormalizer(IHostEnvironment environment)
{
    public ApplicationResourceDefinition Normalize(ApplicationResourceDefinition definition)
    {
        var id = NormalizeApplicationId(definition.Id, definition.Name);
        var resourceType = NormalizeResourceType(definition.ResourceType);
        var isAspNetCoreProject = string.Equals(
            resourceType,
            ApplicationResourceTypes.AspNetCoreProject,
            StringComparison.OrdinalIgnoreCase);
        var isProjectBacked = isAspNetCoreProject || definition.ProjectContainerBuild;
        var legacyProjectPath = isAspNetCoreProject
            ? ApplicationProcessDefinitions.TryExtractProjectPathFromDotNetArguments(definition.Arguments)
            : null;
        var projectPath = isProjectBacked
            ? NormalizeNullable(definition.ProjectPath) ?? legacyProjectPath
            : null;
        var replicasEnabled = IsContainerBacked(definition) &&
            (definition.ReplicasEnabled || definition.Replicas > 1);

        return definition with
        {
            Id = id,
            Name = definition.Name.Trim(),
            ExecutablePath = isProjectBacked ? string.Empty : definition.ExecutablePath.Trim(),
            Arguments = isProjectBacked ? null : NormalizeNullable(definition.Arguments),
            WorkingDirectory = NormalizeNullable(definition.WorkingDirectory),
            Endpoint = NormalizeNullable(definition.Endpoint),
            Lifetime = definition.Lifetime,
            UseServiceDiscovery = definition.UseServiceDiscovery,
            ContainerImage = NormalizeNullable(definition.ContainerImage),
            ContainerRegistry = IsContainerBacked(definition)
                ? NormalizeContainerRegistry(definition.ContainerRegistry)
                : null,
            ContainerRegistryCredentials = IsContainerBacked(definition)
                ? ContainerRegistryCredentials.Normalize(definition.ContainerRegistryCredentials)
                : null,
            ContainerBuildContext = NormalizeNullable(definition.ContainerBuildContext),
            ContainerDockerfile = NormalizeNullable(definition.ContainerDockerfile),
            ProjectContainerBuild = isProjectBacked &&
                string.IsNullOrWhiteSpace(definition.ContainerImage) &&
                definition.ProjectContainerBuild,
            ContainerHostId = NormalizeNullable(definition.ContainerHostId),
            ContainerRevision = NormalizeNullable(definition.ContainerRevision) ??
                (IsContainerBacked(definition) ? CreateContainerRevision() : null),
            Replicas = Math.Max(1, definition.Replicas),
            ReplicasEnabled = replicasEnabled,
            ResourceType = resourceType,
            ProjectPath = projectPath,
            ProjectArguments = isProjectBacked
                ? NormalizeNullable(definition.ProjectArguments) ??
                    ApplicationProcessDefinitions.TryExtractApplicationArgumentsFromDotNetArguments(definition.Arguments)
                : null,
            AspNetCoreHotReload = isProjectBacked
                ? ApplicationProcessDefinitions.ResolveAspNetCoreHotReload(definition)
                : definition.AspNetCoreHotReload,
            UseLaunchSettingsEndpoints = isAspNetCoreProject &&
                definition.UseLaunchSettingsEndpoints,
            DependsOn = NormalizeDependencies(definition.DependsOn, id),
            References = NormalizeReferences(definition.References, id),
            EndpointPorts = ResolveEndpointPorts(
                definition.EndpointPorts,
                resourceType,
                definition.Endpoint,
                projectPath,
                definition.UseLaunchSettingsEndpoints),
            HealthChecks = ApplicationHealthRecoveryDeclarations.NormalizeHealthChecks(definition.HealthChecks),
            RecoveryPolicies = ApplicationHealthRecoveryDeclarations.NormalizeRecoveryPolicies(definition.RecoveryPolicies),
            Observability = NormalizeObservability(definition.Observability),
            AppSettings = ApplicationConfigurationReferences.NormalizeAppSettings(definition.AppSettings),
            EnvironmentVariables = ApplicationConfigurationReferences.NormalizeEnvironmentVariables(definition.EnvironmentVariables),
            VolumeMounts = NormalizeVolumeMounts(definition.VolumeMounts),
            SqlDatabases = NormalizeSqlDatabases(definition.SqlDatabases),
            LogSources = ApplicationLogSources.Normalize(definition.LogSources)
        };
    }

    public ApplicationResourceDefinition Resolve(ApplicationResourceDefinition definition)
    {
        if (!ApplicationResourceTypes.IsAspNetCoreProject(definition.ResourceType) ||
            definition.EndpointPorts.Count > 0)
        {
            return definition;
        }

        var endpointPorts = definition.UseLaunchSettingsEndpoints
            ? TryReadLaunchSettingsEndpointPorts(definition.ProjectPath)
            : [];
        return endpointPorts.Count == 0
            ? definition with
            {
                EndpointPorts = ApplicationProviderServiceCollectionExtensions.CreateAspNetCoreProjectEndpointPorts(definition.Endpoint)
            }
            : definition with { EndpointPorts = endpointPorts };
    }

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

    private static IReadOnlyList<ServicePort> NormalizeEndpointPorts(IReadOnlyList<ServicePort> ports) =>
        ports
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

    private static IReadOnlyList<ResourceVolumeMount> NormalizeVolumeMounts(
        IReadOnlyList<ResourceVolumeMount> volumeMounts) =>
        volumeMounts
            .Where(mount =>
                !string.IsNullOrWhiteSpace(mount.VolumeReference) &&
                !string.IsNullOrWhiteSpace(mount.TargetPath))
            .Select(mount => mount with
            {
                VolumeReference = mount.NormalizedVolumeReference,
                TargetPath = mount.NormalizedTargetPath,
                Name = mount.NormalizedName
            })
            .ToArray();

    private static IReadOnlyList<SqlServerDatabaseDefinition> NormalizeSqlDatabases(
        IReadOnlyList<SqlServerDatabaseDefinition> databases) =>
        databases
            .Where(database => !string.IsNullOrWhiteSpace(database.Name))
            .Select(database => new SqlServerDatabaseDefinition(
                database.Name.Trim(),
                NormalizeNullable(database.DisplayName)))
            .GroupBy(database => database.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

    private static ResourceObservability? NormalizeObservability(ResourceObservability? observability)
    {
        if (observability is null)
        {
            return null;
        }

        var attributes = observability.Attributes
            .Where(attribute => !string.IsNullOrWhiteSpace(attribute.Key))
            .ToDictionary(
                attribute => attribute.Key.Trim(),
                attribute => attribute.Value,
                StringComparer.OrdinalIgnoreCase);

        return observability with
        {
            OtlpEndpoint = NormalizeNullable(observability.OtlpEndpoint),
            OtlpProtocol = NormalizeNullable(observability.OtlpProtocol),
            OtlpHeaders = NormalizeNullable(observability.OtlpHeaders),
            ServiceName = NormalizeNullable(observability.ServiceName),
            ResourceAttributes = attributes.Count == 0 ? null : attributes
        };
    }

    private static bool IsContainerBacked(ApplicationResourceDefinition application) =>
        !string.IsNullOrWhiteSpace(application.ContainerImage) ||
        application.ProjectContainerBuild ||
        !string.IsNullOrWhiteSpace(application.ContainerBuildContext);

    private static string NormalizeContainerRegistry(string? registry) =>
        NormalizeNullable(registry) ?? ContainerRegistryDefaults.Default;

    private static string NormalizeResourceType(string? resourceType) =>
        ApplicationResourceTypes.IsApplication(resourceType)
            ? resourceType!.Trim()
            : ApplicationResourceTypes.ExecutableApplication;

    private static string NormalizeApplicationId(string? id, string name)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return CreateId(name);
        }

        var normalized = id.Trim();
        return normalized.Contains(':', StringComparison.Ordinal)
            ? normalized
            : CreateId(normalized);
    }

    private static IReadOnlyList<string> NormalizeDependencies(
        IReadOnlyList<string> dependsOn,
        string resourceId) =>
        dependsOn
            .Where(dependency => !string.IsNullOrWhiteSpace(dependency))
            .Select(dependency => dependency.Trim())
            .Where(dependency => !string.Equals(dependency, resourceId, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<string> NormalizeReferences(
        IReadOnlyList<string> references,
        string resourceId) =>
        references
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .Select(reference => reference.Trim())
            .Where(reference => !string.Equals(reference, resourceId, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string CreateId(string name) =>
        ResourceId.FromName("application", name).Value;

    private static string NormalizeProtocol(string? protocol) =>
        string.IsNullOrWhiteSpace(protocol) ? "tcp" : protocol.Trim().ToLowerInvariant();

    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string CreateContainerRevision() =>
        $"rev-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..27];
}
