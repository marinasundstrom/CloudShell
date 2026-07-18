namespace CloudShell.ControlPlane.Providers;

public sealed class PythonAppResourceDefinitionBuilder(string name) :
    ContainerizableResourceDefinitionBuilder<PythonAppResourceDefinitionBuilder>(
        name,
        PythonAppResourceTypeProvider.ResourceTypeId,
        PythonAppResourceTypeProvider.ProviderId)
{
    private readonly List<NetworkingEndpointRequestValue> _endpointRequests = [];
    private readonly Dictionary<string, ResourceEnvironmentVariableValue> _environmentVariables =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ResourceReference> _references = [];
    private readonly List<VolumeMountDefinition> _volumeMounts = [];
    private readonly List<ResourceHealthCheckDefinition> _healthChecks = [];

    public PythonAppResourceDefinitionBuilder WithProjectPath(string projectPath) =>
        SetScalarAttribute(PythonAppResourceTypeProvider.Attributes.ProjectPath, projectPath);

    public PythonAppResourceDefinitionBuilder WithCommand(string command) =>
        SetScalarAttribute(PythonAppResourceTypeProvider.Attributes.Command, command);

    public PythonAppResourceDefinitionBuilder WithScriptPath(string scriptPath) =>
        SetScalarAttribute(PythonAppResourceTypeProvider.Attributes.ScriptPath, scriptPath);

    public PythonAppResourceDefinitionBuilder WithModule(string module) =>
        SetScalarAttribute(PythonAppResourceTypeProvider.Attributes.Module, module);

    public PythonAppResourceDefinitionBuilder WithArguments(string arguments) =>
        SetScalarAttribute(PythonAppResourceTypeProvider.Attributes.Arguments, arguments);

    public PythonAppResourceDefinitionBuilder WithServiceDiscoveryName(string name) =>
        SetScalarAttribute(PythonAppResourceTypeProvider.Attributes.ServiceDiscoveryName, name);

    public PythonAppResourceDefinitionBuilder AsContainerApp(
        string? image = null,
        string? registry = null,
        string? tag = null,
        string? buildContext = null,
        string? dockerfile = null)
    {
        var effectiveBuildContext = FirstNonEmpty(
            buildContext,
            Attributes.TryGetValue(
                PythonAppResourceTypeProvider.Attributes.ProjectPath,
                out var projectPath)
                    ? projectPath.StringValue
                    : null);
        return ProjectAsContainerApplication(
            FirstNonEmpty(image, CreateDefaultContainerImage(Name, tag))!,
            registry,
            effectiveBuildContext,
            dockerfile,
            PythonAppResourceTypeProvider.Attributes.EndpointRequests,
            _endpointRequests);
    }

    public PythonAppResourceDefinitionBuilder WithReplicas(long replicas) =>
        SetContainerReplicas(replicas);

    public PythonAppResourceDefinitionBuilder WithRuntimeMonitoring() =>
        this;

    public PythonAppResourceDefinitionBuilder WithDefaultConsoleLogSource() =>
        SetObjectAttribute(
            ResourceLogSourceAttributeIds.LogSources,
            ResourceLogSourceDefinitionSet.DefaultConsole());

    public PythonAppResourceDefinitionBuilder AddEndpointRequest(
        string name,
        string protocol,
        int? targetPort = null,
        string? host = null,
        int? port = null,
        string? exposure = null,
        string? ipAddress = null,
        IResourceDefinitionBuilder? network = null,
        string? assignment = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(protocol);

        var effectiveNetwork = network ?? ResourceGraph?.GetDefaultNetwork();

        _endpointRequests.Add(new NetworkingEndpointRequestValue(
            name.Trim(),
            protocol.Trim(),
            TargetPort: targetPort,
            Host: string.IsNullOrWhiteSpace(host) ? null : host.Trim(),
            Port: port,
            IpAddress: string.IsNullOrWhiteSpace(ipAddress) ? null : ipAddress.Trim(),
            Exposure: string.IsNullOrWhiteSpace(exposure) ? null : exposure.Trim(),
            Assignment: string.IsNullOrWhiteSpace(assignment) ? null : assignment.Trim(),
            Network: effectiveNetwork is null
                ? null
                : ResourceReference.ReferenceResourceId(
                    effectiveNetwork.EffectiveResourceId,
                    effectiveNetwork.ResourceTypeId,
                    effectiveNetwork.ResourceProviderId)));
        return SetObjectAttribute(
            TypeId == ContainerApplicationResourceTypeProvider.ResourceTypeId
                ? ContainerApplicationResourceTypeProvider.Attributes.EndpointRequests
                : PythonAppResourceTypeProvider.Attributes.EndpointRequests,
            _endpointRequests.ToArray());
    }

    public PythonAppResourceDefinitionBuilder WithHttpEndpoint(
        int? port = null,
        int? targetPort = null,
        string name = "http",
        string? host = null,
        string exposure = "Local",
        string? ipAddress = null,
        IResourceDefinitionBuilder? network = null,
        string? assignment = null) =>
        AddEndpointRequest(
            name,
            "http",
            targetPort,
            host,
            port,
            exposure,
            ipAddress,
            network,
            assignment);

    public PythonAppResourceDefinitionBuilder WithEnvironmentVariable(
        string name,
        string? value = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        _environmentVariables[name.Trim()] = new(
            string.IsNullOrWhiteSpace(value) ? null : value.Trim());
        return SetObjectAttribute(
            EnvironmentVariablesCapabilityProvider.AttributeId,
            _environmentVariables);
    }

    public PythonAppResourceDefinitionBuilder WithEnvironmentVariable(
        string name,
        ResourceConfigurationSettingReference configurationSetting)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configurationSetting);

        _environmentVariables[name.Trim()] = new(
            ConfigurationSettingRef: configurationSetting);
        return SetObjectAttribute(
            EnvironmentVariablesCapabilityProvider.AttributeId,
            _environmentVariables);
    }

    public PythonAppResourceDefinitionBuilder WithEnvironmentVariable(
        string name,
        ResourceSecretReference secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(secret);

        _environmentVariables[name.Trim()] = new(
            SecretRef: secret);
        return SetObjectAttribute(
            EnvironmentVariablesCapabilityProvider.AttributeId,
            _environmentVariables);
    }

    public PythonAppResourceDefinitionBuilder WithReference(
        IResourceDefinitionBuilder resource,
        ResourceTypeId? typeId = null,
        string? providerId = null)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return WithReference(
            resource.EffectiveResourceId,
            typeId ?? resource.ResourceTypeId,
            providerId ?? resource.ResourceProviderId);
    }

    public PythonAppResourceDefinitionBuilder WithReference(
        string resourceId,
        ResourceTypeId? typeId = null,
        string? providerId = null)
    {
        _references.Add(ResourceReference.ReferenceResourceId(resourceId, typeId, providerId));
        return SetObjectAttribute(
            PythonAppResourceTypeProvider.Attributes.References,
            _references.ToArray());
    }

    public PythonAppResourceDefinitionBuilder MountVolume(
        IResourceDefinitionBuilder volume,
        string targetPath,
        bool readOnly = false)
    {
        ArgumentNullException.ThrowIfNull(volume);

        return MountVolume(volume.EffectiveResourceId, targetPath, readOnly);
    }

    public PythonAppResourceDefinitionBuilder MountVolume(
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
        return SetObjectAttribute(
            ResourceAttributeId.Create(VolumeConsumerCapabilityProvider.CapabilityIdValue.ToString()),
            new VolumeConsumerDefinition(_volumeMounts.ToArray()));
    }

    public PythonAppResourceDefinitionBuilder AddHealthCheck(
        ResourceHealthCheckDefinition check)
    {
        ArgumentNullException.ThrowIfNull(check);

        _healthChecks.Add(check);
        return SetObjectAttribute(
            ResourceHealthCheckAttributeIds.HealthChecks,
            new ResourceHealthCheckDefinitionSet(_healthChecks.ToArray()));
    }

    private static string CreateDefaultContainerImage(string name, string? tag)
    {
        var normalizedName = new string(name
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray()).Trim('-');
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            normalizedName = "app";
        }

        return $"cloudshell-python-{normalizedName}:{FirstNonEmpty(tag, "dev")}";
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}

public static class PythonAppResourceDefinitionBuilderExtensions
{
    public static PythonAppResourceDefinitionBuilder WithServiceDiscovery(
        this PythonAppResourceDefinitionBuilder builder,
        bool enabled = true)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return enabled
            ? builder.WithServiceDiscoveryName(builder.Name)
            : builder;
    }

    public static PythonAppResourceDefinitionBuilder WithHttpHealthCheck(
        this PythonAppResourceDefinitionBuilder builder,
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

    public static PythonAppResourceDefinitionBuilder WithHttpLivenessCheck(
        this PythonAppResourceDefinitionBuilder builder,
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

    public static PythonAppResourceDefinitionBuilder WithHttpProbe(
        this PythonAppResourceDefinitionBuilder builder,
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

    public static PythonAppResourceDefinitionBuilder AddPythonApp(
        this ResourceGraphBuilder graph,
        string name,
        string projectPath,
        string scriptPath = "app.py")
    {
        ArgumentNullException.ThrowIfNull(graph);

        graph.AddResourceTypeDefinition(new PythonAppResourceTypeProvider().TypeDefinition);

        var builder = new PythonAppResourceDefinitionBuilder(name)
            .WithProjectPath(projectPath)
            .WithCommand("python3")
            .WithScriptPath(scriptPath)
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

public sealed record PythonAppEnvironmentVariableValue(
    [property: System.Text.Json.Serialization.JsonConverter(
        typeof(ApplicationEnvironmentVariableValueJsonConverter))]
    string? Value = null,
    ResourceConfigurationSettingReference? ConfigurationSettingRef = null,
    ResourceSecretReference? SecretRef = null);
