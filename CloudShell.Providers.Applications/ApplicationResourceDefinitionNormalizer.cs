using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.Hosting;

namespace CloudShell.Providers.Applications;

public sealed class ApplicationResourceDefinitionNormalizer(
    IHostEnvironment environment,
    IEnumerable<IApplicationResourceDefinitionNormalizationRule>? rules = null)
{
    private readonly ApplicationResourceDefinitionNormalizationContext context = new(environment);
    private readonly IReadOnlyList<IApplicationResourceDefinitionNormalizationRule> rules = CreateRules(rules);

    public ApplicationResourceDefinition Normalize(ApplicationResourceDefinition definition)
    {
        var id = NormalizeApplicationId(definition.Id, definition.Name);
        var resourceType = NormalizeResourceType(definition.ResourceType);

        var normalized = definition with
        {
            Id = id,
            Name = definition.Name.Trim(),
            ExecutablePath = definition.ExecutablePath.Trim(),
            Arguments = NormalizeNullable(definition.Arguments),
            WorkingDirectory = NormalizeNullable(definition.WorkingDirectory),
            Endpoint = NormalizeNullable(definition.Endpoint),
            Lifetime = definition.Lifetime,
            UseServiceDiscovery = definition.UseServiceDiscovery,
            ContainerImage = NormalizeNullable(definition.ContainerImage),
            ContainerRegistry = NormalizeNullable(definition.ContainerRegistry),
            ContainerRegistryCredentials = definition.ContainerRegistryCredentials,
            ContainerBuildContext = NormalizeNullable(definition.ContainerBuildContext),
            ContainerDockerfile = NormalizeNullable(definition.ContainerDockerfile),
            ProjectContainerBuild = definition.ProjectContainerBuild,
            ContainerHostId = NormalizeNullable(definition.ContainerHostId),
            ContainerRevision = NormalizeNullable(definition.ContainerRevision),
            ReplicaManagementPolicy = NormalizeReplicaManagementPolicy(definition.ReplicaManagementPolicy),
            Replicas = Math.Max(1, definition.Replicas),
            ReplicasEnabled = definition.ReplicasEnabled,
            ResourceType = resourceType,
            ProjectPath = NormalizeNullable(definition.ProjectPath),
            ProjectArguments = NormalizeNullable(definition.ProjectArguments),
            AspNetCoreHotReload = definition.AspNetCoreHotReload,
            UseLaunchSettingsEndpoints = definition.UseLaunchSettingsEndpoints,
            EndpointPorts = NormalizeEndpointPorts(definition.EndpointPorts),
            SqlDatabases = definition.SqlDatabases,
        };

        normalized = ApplyRules(normalized);

        return normalized with
        {
            DependsOn = NormalizeDependencies(normalized.DependsOn, normalized.Id),
            References = NormalizeReferences(normalized.References, normalized.Id),
            EndpointPorts = NormalizeEndpointPorts(normalized.EndpointPorts),
            HealthChecks = ApplicationHealthRecoveryDeclarations.NormalizeHealthChecks(normalized.HealthChecks),
            RecoveryPolicies = ApplicationHealthRecoveryDeclarations.NormalizeRecoveryPolicies(normalized.RecoveryPolicies),
            Observability = NormalizeObservability(normalized.Observability),
            AppSettings = ApplicationConfigurationReferences.NormalizeAppSettings(normalized.AppSettings),
            EnvironmentVariables = ApplicationConfigurationReferences.NormalizeEnvironmentVariables(normalized.EnvironmentVariables),
            VolumeMounts = NormalizeVolumeMounts(normalized.VolumeMounts),
            SqlDatabases = normalized.SqlDatabases,
            LogSources = ApplicationLogSources.Normalize(normalized.LogSources)
        };
    }

    private static ResourceOrchestratorReplicaManagementPolicy? NormalizeReplicaManagementPolicy(
        ResourceOrchestratorReplicaManagementPolicy? policy) =>
        policy is null
            ? null
            : policy with
            {
                FailureThreshold = Math.Max(1, policy.FailureThreshold),
                InitialBackoffSeconds = Math.Max(0, policy.InitialBackoffSeconds),
                MaxBackoffSeconds = Math.Max(0, policy.MaxBackoffSeconds),
                BackoffMultiplier = Math.Max(1, policy.BackoffMultiplier),
                MaxAttempts = Math.Max(0, policy.MaxAttempts),
                ResetAfterHealthySeconds = Math.Max(0, policy.ResetAfterHealthySeconds)
            };

    public ApplicationResourceDefinition Resolve(ApplicationResourceDefinition definition)
    {
        var resolved = definition;
        foreach (var rule in rules)
        {
            if (rule.AppliesTo(resolved))
            {
                resolved = rule.Resolve(resolved, context);
            }
        }

        return resolved;
    }

    private ApplicationResourceDefinition ApplyRules(ApplicationResourceDefinition definition)
    {
        var normalized = definition;
        foreach (var rule in rules)
        {
            if (rule.AppliesTo(normalized))
            {
                normalized = rule.Normalize(normalized, context);
            }
        }

        return normalized;
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

    private static string NormalizeResourceType(string? resourceType) =>
        string.IsNullOrWhiteSpace(resourceType)
            ? ApplicationResourceTypes.ExecutableApplication
            : resourceType.Trim();

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

    private static IReadOnlyList<IApplicationResourceDefinitionNormalizationRule> CreateRules(
        IEnumerable<IApplicationResourceDefinitionNormalizationRule>? rules)
    {
        var configuredRules = rules?.ToArray();
        if (configuredRules is { Length: > 0 })
        {
            return configuredRules;
        }

        return
        [
            new ProjectBackedApplicationResourceDefinitionNormalizationRule(),
            new AspNetCoreProjectEndpointNormalizationRule(),
            new ContainerBackedApplicationResourceDefinitionNormalizationRule(),
            new SqlServerApplicationResourceDefinitionNormalizationRule()
        ];
    }
}
