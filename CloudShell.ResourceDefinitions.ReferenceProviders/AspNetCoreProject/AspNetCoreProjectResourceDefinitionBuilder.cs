namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class AspNetCoreProjectResourceDefinitionBuilder(string name) :
    ResourceDefinitionBuilder<AspNetCoreProjectResourceDefinitionBuilder>(name)
{
    private readonly List<NetworkingEndpointRequestValue> _endpointRequests = [];
    private readonly Dictionary<string, AspNetCoreProjectEnvironmentVariableValue> _environmentVariables =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ResourceReference> _references = [];
    private readonly List<VolumeMountDefinition> _volumeMounts = [];
    private readonly List<ResourceHealthCheckDefinition> _healthChecks = [];

    protected override ResourceTypeId TypeId =>
        AspNetCoreProjectResourceTypeProvider.ResourceTypeId;

    protected override string? ProviderId =>
        AspNetCoreProjectResourceTypeProvider.ProviderId;

    public AspNetCoreProjectResourceDefinitionBuilder WithProjectPath(string projectPath) =>
        SetScalarAttribute(AspNetCoreProjectResourceTypeProvider.Attributes.ProjectPath, projectPath);

    public AspNetCoreProjectResourceDefinitionBuilder WithArguments(string arguments) =>
        SetScalarAttribute(AspNetCoreProjectResourceTypeProvider.Attributes.ProjectArguments, arguments);

    public AspNetCoreProjectResourceDefinitionBuilder WithHotReload(bool enabled = true) =>
        SetScalarAttribute(AspNetCoreProjectResourceTypeProvider.Attributes.HotReload, enabled);

    public AspNetCoreProjectResourceDefinitionBuilder UseLaunchSettings(bool enabled = true) =>
        SetScalarAttribute(AspNetCoreProjectResourceTypeProvider.Attributes.UseLaunchSettings, enabled);

    public AspNetCoreProjectResourceDefinitionBuilder WithServiceDiscoveryName(string name) =>
        SetScalarAttribute(AspNetCoreProjectResourceTypeProvider.Attributes.ServiceDiscoveryName, name);

    public AspNetCoreProjectResourceDefinitionBuilder WithRuntimeMonitoring() =>
        DeclareCapability(ResourceCommonCapabilityIds.Monitoring);

    public AspNetCoreProjectResourceDefinitionBuilder WithDefaultConsoleLogSource() =>
        SetCapability(
            ResourceLogSourceCapabilityIds.LogSources,
            ResourceLogSourceDefinitionSet.DefaultConsole());

    public AspNetCoreProjectResourceDefinitionBuilder AddEndpointRequest(
        string name,
        string protocol,
        int? targetPort = null,
        string? host = null,
        int? port = null,
        string? exposure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(protocol);

        _endpointRequests.Add(new NetworkingEndpointRequestValue(
            name.Trim(),
            protocol.Trim(),
            TargetPort: targetPort,
            Host: string.IsNullOrWhiteSpace(host) ? null : host.Trim(),
            Port: port,
            Exposure: string.IsNullOrWhiteSpace(exposure) ? null : exposure.Trim()));
        return SetObjectAttribute(
            AspNetCoreProjectResourceTypeProvider.Attributes.EndpointRequests,
            _endpointRequests.ToArray());
    }

    public AspNetCoreProjectResourceDefinitionBuilder WithEnvironmentVariable(
        string name,
        string? value = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        _environmentVariables[name.Trim()] = new(
            string.IsNullOrWhiteSpace(value) ? null : value.Trim());
        return SetObjectAttribute(
            AspNetCoreProjectResourceTypeProvider.Attributes.EnvironmentVariables,
            _environmentVariables);
    }

    public AspNetCoreProjectResourceDefinitionBuilder WithEnvironmentVariable(
        string name,
        ResourceConfigurationEntryReference configurationEntry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configurationEntry);

        _environmentVariables[name.Trim()] = new(
            ConfigurationEntryRef: configurationEntry);
        return SetObjectAttribute(
            AspNetCoreProjectResourceTypeProvider.Attributes.EnvironmentVariables,
            _environmentVariables);
    }

    public AspNetCoreProjectResourceDefinitionBuilder WithEnvironmentVariable(
        string name,
        ResourceSecretReference secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(secret);

        _environmentVariables[name.Trim()] = new(
            SecretRef: secret);
        return SetObjectAttribute(
            AspNetCoreProjectResourceTypeProvider.Attributes.EnvironmentVariables,
            _environmentVariables);
    }

    public AspNetCoreProjectResourceDefinitionBuilder WithReference(
        IResourceDefinitionBuilder resource,
        ResourceTypeId? typeId = null,
        string? providerId = null)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return WithReference(resource.EffectiveResourceId, typeId, providerId);
    }

    public AspNetCoreProjectResourceDefinitionBuilder WithReference(
        string resourceId,
        ResourceTypeId? typeId = null,
        string? providerId = null)
    {
        _references.Add(ResourceReference.ReferenceResourceId(resourceId, typeId, providerId));
        return SetObjectAttribute(
            AspNetCoreProjectResourceTypeProvider.Attributes.References,
            _references.ToArray());
    }

    public AspNetCoreProjectResourceDefinitionBuilder MountVolume(
        IResourceDefinitionBuilder volume,
        string targetPath,
        bool readOnly = false)
    {
        ArgumentNullException.ThrowIfNull(volume);

        return MountVolume(volume.EffectiveResourceId, targetPath, readOnly);
    }

    public AspNetCoreProjectResourceDefinitionBuilder MountVolume(
        string volumeResourceId,
        string targetPath,
        bool readOnly = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(volumeResourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        _volumeMounts.Add(new(
            volumeResourceId.Trim(),
            targetPath.Trim(),
            readOnly));
        return SetCapability(
            VolumeConsumerCapabilityProvider.CapabilityIdValue,
            new VolumeConsumerDefinition(_volumeMounts.ToArray()));
    }

    public AspNetCoreProjectResourceDefinitionBuilder AddHealthCheck(
        ResourceHealthCheckDefinition check)
    {
        ArgumentNullException.ThrowIfNull(check);

        _healthChecks.Add(check);
        return SetCapability(
            ResourceHealthCheckCapabilityIds.HealthChecks,
            new ResourceHealthCheckDefinitionSet(_healthChecks.ToArray()));
    }
}

public static class AspNetCoreProjectResourceDefinitionBuilderExtensions
{
    public static AspNetCoreProjectResourceDefinitionBuilder WithServiceDiscovery(
        this AspNetCoreProjectResourceDefinitionBuilder builder,
        bool enabled = true)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return enabled
            ? builder.WithServiceDiscoveryName(builder.Name)
            : builder;
    }

    public static AspNetCoreProjectResourceDefinitionBuilder WithHttpHealthCheck(
        this AspNetCoreProjectResourceDefinitionBuilder builder,
        string path,
        string? endpointName = null,
        string name = ResourceHealthCheckDefinitionValues.Health,
        TimeSpan? timeout = null,
        TimeSpan? interval = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithHttpProbe(
            ResourceHealthCheckDefinitionValues.Health,
            path,
            endpointName,
            name,
            timeout,
            interval);
    }

    public static AspNetCoreProjectResourceDefinitionBuilder WithHttpLivenessCheck(
        this AspNetCoreProjectResourceDefinitionBuilder builder,
        string path,
        string? endpointName = null,
        string name = "alive",
        TimeSpan? timeout = null,
        TimeSpan? interval = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithHttpProbe(
            ResourceHealthCheckDefinitionValues.Liveness,
            path,
            endpointName,
            name,
            timeout,
            interval);
    }

    public static AspNetCoreProjectResourceDefinitionBuilder WithHttpProbe(
        this AspNetCoreProjectResourceDefinitionBuilder builder,
        string type,
        string path,
        string? endpointName = null,
        string? name = null,
        TimeSpan? timeout = null,
        TimeSpan? interval = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return builder.AddHealthCheck(new ResourceHealthCheckDefinition(
            string.IsNullOrWhiteSpace(name) ? type.Trim() : name.Trim(),
            type.Trim(),
            ResourceProbeSourceDefinition.ForHttp(
                path,
                endpointName,
                ToMilliseconds(timeout)),
            ToSeconds(interval)));
    }

    public static AspNetCoreProjectResourceDefinitionBuilder AddAspNetCoreProject(
        this ResourceDefinitionGraphBuilder graph,
        string name,
        string projectPath)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var builder = new AspNetCoreProjectResourceDefinitionBuilder(name)
            .WithProjectPath(projectPath)
            .WithRuntimeMonitoring()
            .WithDefaultConsoleLogSource();
        graph.Add(builder);
        return builder;
    }

    private static int? ToMilliseconds(TimeSpan? value) =>
        value is null ? null : Math.Max(1, (int)Math.Ceiling(value.Value.TotalMilliseconds));

    private static int? ToSeconds(TimeSpan? value) =>
        value is null ? null : Math.Max(1, (int)Math.Ceiling(value.Value.TotalSeconds));
}
