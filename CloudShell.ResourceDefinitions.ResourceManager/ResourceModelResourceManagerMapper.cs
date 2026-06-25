using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using ResourceManagerClass = CloudShell.Abstractions.ResourceManager.ResourceClass;
using ResourceManagerState = CloudShell.Abstractions.ResourceManager.ResourceState;
using ResourceModelResource = CloudShell.ResourceDefinitions.Resource;
using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;

namespace CloudShell.ResourceDefinitions.ResourceManager;

public sealed record ResourceModelResourceManagerProjectionOptions(
    string DefaultProviderId = ResourceModelResourceProvider.DefaultProviderId,
    string DefaultRegion = ResourceModelResourceProvider.DefaultRegion,
    DateTimeOffset? DefaultLastUpdated = null,
    string BridgeProviderId = ResourceModelResourceProvider.DefaultProviderId,
    ResourceModelResourceManagerStateResolver? StateResolver = null,
    ResourceModelResourceManagerEndpointProjectionResolver? EndpointProjectionResolver = null);

public delegate ResourceManagerState? ResourceModelResourceManagerStateResolver(
    ResourceModelResource resource);

public delegate ResourceModelResourceManagerEndpointProjection? ResourceModelResourceManagerEndpointProjectionResolver(
    ResourceModelResource resource);

public sealed record ResourceModelResourceManagerEndpointProjection(
    IReadOnlyList<ResourceEndpoint>? Endpoints = null,
    IReadOnlyList<ResourceEndpointMappingDefinition>? EndpointMappings = null,
    IReadOnlyList<ResourceEndpointNetworkMapping>? EndpointNetworkMappings = null)
{
    public static ResourceModelResourceManagerEndpointProjection Empty { get; } = new();

    public IReadOnlyList<ResourceEndpoint> ResourceEndpoints => Endpoints ?? [];

    public IReadOnlyList<ResourceEndpointMappingDefinition> ResourceEndpointMappings =>
        EndpointMappings ?? [];

    public IReadOnlyList<ResourceEndpointNetworkMapping> ResourceEndpointNetworkMappings =>
        EndpointNetworkMappings ?? [];
}

public static class ResourceModelResourceManagerAttributeNames
{
    public const string BridgeProviderId = "resourceModel.bridgeProviderId";
}

public static class ResourceModelResourceManagerMapper
{
    public static ResourceManagerResource ToResourceManagerResource(
        ResourceModelResource resource,
        ResourceModelResourceManagerProjectionOptions? options = null,
        IReadOnlyList<string>? dependencyIds = null)
    {
        ArgumentNullException.ThrowIfNull(resource);

        options ??= new ResourceModelResourceManagerProjectionOptions();
        var attributes = ToResourceManagerAttributes(resource);
        attributes[ResourceModelResourceManagerAttributeNames.BridgeProviderId] =
            options.BridgeProviderId;
        var healthChecks = ToResourceManagerHealthChecks(resource);
        var endpointProjection = ToResourceManagerEndpointProjection(resource, options);

        return new ResourceManagerResource(
            resource.EffectiveResourceId,
            resource.Name,
            resource.Type.TypeId.ToString(),
            resource.State.ProviderId ?? resource.Type.Definition.DefaultProviderId ?? options.DefaultProviderId,
            options.DefaultRegion,
            State: ToResourceManagerState(resource, options),
            Endpoints: endpointProjection.ResourceEndpoints,
            resource.Version ?? resource.Revision.ToString(),
            resource.LastModifiedAt ?? resource.CreatedAt ?? options.DefaultLastUpdated ?? DateTimeOffset.UnixEpoch,
            dependencyIds ?? resource.State.StartupDependencyIds,
            TypeId: resource.Type.TypeId.ToString(),
            Actions: resource.Operations
                .Where(operation => operation.IsAvailable)
                .Select(ToResourceManagerAction)
                .ToArray(),
            HealthChecks: healthChecks,
            ResourceClass: ToResourceManagerClass(resource.Class.ClassId),
            Attributes: attributes,
            Capabilities: ToResourceManagerCapabilities(resource, healthChecks),
            Source: ResourceSource.User,
            ManagementMode: ResourceManagementMode.UserManaged,
            DisplayName: resource.State.DisplayName,
            EndpointMappings: endpointProjection.ResourceEndpointMappings,
            EndpointNetworkMappings: endpointProjection.ResourceEndpointNetworkMappings,
            LogSources: ToResourceManagerLogSources(resource));
    }

    public static IReadOnlyList<ResourceModelDiagnostic> ToResourceModelDiagnostics(
        ResourceModelResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return resource.Diagnostics
            .Select(diagnostic => new ResourceModelDiagnostic(
                diagnostic.Code,
                ToResourceModelDiagnosticMessage(diagnostic),
                resource.EffectiveResourceId,
                resource.Type.TypeId.ToString(),
                ToResourceManagerClass(resource.Class.ClassId),
                ToResourceManagerClass(resource.Class.ClassId),
                "resource model"))
            .ToArray();
    }

    private static Dictionary<string, string> ToResourceManagerAttributes(
        ResourceModelResource resource)
    {
        var attributes = resource.Attributes
            .Where(attribute => attribute.Value is not null)
            .ToDictionary(
                attribute => attribute.Name.ToString(),
                attribute => attribute.Value!,
                StringComparer.OrdinalIgnoreCase);

        attributes[ResourceAttributeNames.ResourceGraphMembership] = ResourceGraphMembershipKinds.Declared;

        return attributes;
    }

    private static ResourceManagerClass ToResourceManagerClass(ResourceClassId classId) =>
        Enum.TryParse<ResourceManagerClass>(classId.ToString(), ignoreCase: true, out var resourceClass)
            ? resourceClass
            : ResourceManagerClass.Generic;

    private static ResourceManagerState? ToResourceManagerState(
        ResourceModelResource resource,
        ResourceModelResourceManagerProjectionOptions options) =>
        options.StateResolver?.Invoke(resource) ??
        (resource.Operations.Any(operation => IsLifecycleOperation(operation.Id))
            ? ResourceManagerState.Unknown
            : null);

    private static ResourceModelResourceManagerEndpointProjection ToResourceManagerEndpointProjection(
        ResourceModelResource resource,
        ResourceModelResourceManagerProjectionOptions options) =>
        options.EndpointProjectionResolver?.Invoke(resource) ??
        ResourceModelResourceManagerEndpointProjection.Empty;

    private static bool IsLifecycleOperation(ResourceOperationId operationId) =>
        operationId.ToString() is
            ResourceActionIds.Start or
            ResourceActionIds.Stop or
            ResourceActionIds.Pause or
            ResourceActionIds.Restart;

    private static ResourceAction ToResourceManagerAction(ResourceOperationResolution operation) =>
        operation.Id.ToString() switch
        {
            ResourceActionIds.Start => ResourceAction.Start,
            ResourceActionIds.Stop => ResourceAction.Stop,
            ResourceActionIds.Pause => ResourceAction.Pause,
            ResourceActionIds.Restart => ResourceAction.Restart,
            var id => new ResourceAction(
                id,
                ToDisplayName(id),
                Description: "Resolved Resource model operation.")
        };

    private static IReadOnlyList<ResourceHealthCheck> ToResourceManagerHealthChecks(
        ResourceModelResource resource)
    {
        var definitions = resource.Capabilities.Get<ResourceHealthCheckDefinitionSet>(
            ResourceHealthCheckCapabilityIds.HealthChecks);

        if (definitions?.Checks is not { Count: > 0 } checks)
        {
            return [];
        }

        return checks
            .Select(ToResourceManagerHealthCheck)
            .Where(check => check is not null)
            .Cast<ResourceHealthCheck>()
            .ToArray();
    }

    private static ResourceHealthCheck? ToResourceManagerHealthCheck(
        ResourceHealthCheckDefinition definition)
    {
        if (!string.Equals(
                definition.Source.Kind,
                ResourceHealthCheckDefinitionValues.Http,
                StringComparison.OrdinalIgnoreCase) ||
            definition.Source.Http is not { } http ||
            string.IsNullOrWhiteSpace(http.Path))
        {
            return null;
        }

        var timeout = http.TimeoutMilliseconds is > 0
            ? TimeSpan.FromMilliseconds(http.TimeoutMilliseconds.Value)
            : (TimeSpan?)null;
        var source = ResourceProbeSource.ForHttp(
            http.Path,
            http.EndpointName,
            timeout);

        return new ResourceHealthCheck(
            source,
            ParseEnum(definition.Type, ResourceProbeType.Health),
            string.IsNullOrWhiteSpace(definition.Name) ? "health" : definition.Name,
            definition.IntervalSeconds);
    }

    private static IReadOnlyList<ResourceCapability> ToResourceManagerCapabilities(
        ResourceModelResource resource,
        IReadOnlyList<ResourceHealthCheck> healthChecks)
    {
        var capabilities = resource.Capabilities
            .Select(capability => new ResourceCapability(capability.Id.ToString()))
            .ToList();

        if (healthChecks.Any(check => check.Type == ResourceProbeType.Liveness) &&
            capabilities.All(capability =>
                !string.Equals(
                    capability.Id,
                    ResourceHealthCheckCapabilityIds.Liveness.ToString(),
                    StringComparison.OrdinalIgnoreCase)))
        {
            capabilities.Add(new ResourceCapability(ResourceHealthCheckCapabilityIds.Liveness.ToString()));
        }

        return capabilities;
    }

    private static IReadOnlyList<ResourceLogSource> ToResourceManagerLogSources(
        ResourceModelResource resource)
    {
        var definitions = resource.Capabilities.Get<ResourceLogSourceDefinitionSet>(
            ResourceLogSourceCapabilityIds.LogSources);

        if (definitions?.Sources is not { Count: > 0 } sources)
        {
            return [];
        }

        return sources
            .Where(source =>
                !string.IsNullOrWhiteSpace(source.Id) &&
                !string.IsNullOrWhiteSpace(source.Name))
            .Select(ToResourceManagerLogSource)
            .ToArray();
    }

    private static ResourceLogSource ToResourceManagerLogSource(
        ResourceLogSourceDefinition definition) =>
        new(
            definition.Id,
            definition.Name,
            ParseEnum(definition.Kind, ResourceLogSourceKind.ProviderDefined),
            Format: ParseEnum(definition.Format, LogFormat.PlainText),
            Capabilities: ToLogSourceCapabilities(definition.Capabilities),
            Location: definition.Location,
            ProducerResourceId: definition.ProducerResourceId,
            Description: definition.Description,
            Origin: ParseEnum(definition.Origin, ResourceLogSourceOrigin.ProviderDefault),
            Purpose: ParseEnum(definition.Purpose, ResourceLogSourcePurpose.Discovery),
            Availability: ParseEnum(definition.Availability, LogSourceAvailability.Unknown));

    private static LogSourceCapabilities ToLogSourceCapabilities(
        IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return LogSourceCapabilities.Read;
        }

        var capabilities = LogSourceCapabilities.None;
        foreach (var value in values)
        {
            capabilities |= ParseEnum(value, LogSourceCapabilities.None);
        }

        return capabilities == LogSourceCapabilities.None
            ? LogSourceCapabilities.Read
            : capabilities;
    }

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback)
        where TEnum : struct, Enum =>
        !string.IsNullOrWhiteSpace(value) &&
        Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed)
            ? parsed
            : fallback;

    private static string ToDisplayName(string operationId) =>
        string.Join(
            " ",
            operationId
                .Replace('.', ' ')
                .Replace('-', ' ')
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    private static string ToResourceModelDiagnosticMessage(
        ResourceDefinitionDiagnostic diagnostic) =>
        string.IsNullOrWhiteSpace(diagnostic.Target)
            ? diagnostic.Message
            : $"{diagnostic.Message} Target: {diagnostic.Target}.";
}
