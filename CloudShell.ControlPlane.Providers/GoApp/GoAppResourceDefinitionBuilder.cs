namespace CloudShell.ControlPlane.Providers;

public sealed class GoAppResourceDefinitionBuilder(string name) :
    ContainerizableResourceDefinitionBuilder<GoAppResourceDefinitionBuilder>(
        name,
        GoAppResourceTypeProvider.ResourceTypeId,
        GoAppResourceTypeProvider.ProviderId)
{
    private readonly List<NetworkingEndpointRequestValue> _endpointRequests = [];
    private readonly Dictionary<string, ResourceEnvironmentVariableValue> _environmentVariables =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ResourceReference> _references = [];
    private readonly List<VolumeMountDefinition> _volumeMounts = [];
    private readonly List<ResourceHealthCheckDefinition> _healthChecks = [];

    public GoAppResourceDefinitionBuilder WithProjectPath(string projectPath) =>
        SetScalarAttribute(GoAppResourceTypeProvider.Attributes.ProjectPath, projectPath);

    public GoAppResourceDefinitionBuilder WithCommand(string command) =>
        SetScalarAttribute(GoAppResourceTypeProvider.Attributes.Command, command);

    public GoAppResourceDefinitionBuilder WithPackagePath(string packagePath) =>
        SetScalarAttribute(GoAppResourceTypeProvider.Attributes.PackagePath, packagePath);

    public GoAppResourceDefinitionBuilder WithBinaryPath(string binaryPath) =>
        SetScalarAttribute(GoAppResourceTypeProvider.Attributes.BinaryPath, binaryPath);

    public GoAppResourceDefinitionBuilder WithArguments(string arguments) =>
        SetScalarAttribute(GoAppResourceTypeProvider.Attributes.Arguments, arguments);

    public GoAppResourceDefinitionBuilder WithServiceDiscoveryName(string name) =>
        SetScalarAttribute(GoAppResourceTypeProvider.Attributes.ServiceDiscoveryName, name);

    public GoAppResourceDefinitionBuilder AsContainerApp(
        string? image = null,
        string? registry = null,
        string? tag = null,
        string? buildContext = null,
        string? dockerfile = null)
    {
        var effectiveBuildContext = FirstNonEmpty(
            buildContext,
            Attributes.TryGetValue(
                GoAppResourceTypeProvider.Attributes.ProjectPath,
                out var projectPath)
                    ? projectPath.StringValue
                    : null);
        return ProjectAsContainerApplication(
            FirstNonEmpty(image, CreateDefaultContainerImage(Name, tag))!,
            registry,
            effectiveBuildContext,
            dockerfile,
            GoAppResourceTypeProvider.Attributes.EndpointRequests,
            _endpointRequests);
    }

    public GoAppResourceDefinitionBuilder WithReplicas(long replicas) =>
        SetContainerReplicas(replicas);

    public GoAppResourceDefinitionBuilder WithRuntimeMonitoring() =>
        this;

    public GoAppResourceDefinitionBuilder WithDefaultConsoleLogSource() =>
        SetObjectAttribute(
            ResourceLogSourceAttributeIds.LogSources,
            ResourceLogSourceDefinitionSet.DefaultConsole());

    public GoAppResourceDefinitionBuilder AddEndpointRequest(
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
                : GoAppResourceTypeProvider.Attributes.EndpointRequests,
            _endpointRequests.ToArray());
    }

    public GoAppResourceDefinitionBuilder WithHttpEndpoint(
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

    public GoAppResourceDefinitionBuilder WithEnvironmentVariable(
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

    public GoAppResourceDefinitionBuilder WithEnvironmentVariable(
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

    public GoAppResourceDefinitionBuilder WithEnvironmentVariable(
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

    public GoAppResourceDefinitionBuilder WithReference(
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

    public GoAppResourceDefinitionBuilder WithReference(
        string resourceId,
        ResourceTypeId? typeId = null,
        string? providerId = null)
    {
        _references.Add(ResourceReference.ReferenceResourceId(resourceId, typeId, providerId));
        return SetObjectAttribute(
            GoAppResourceTypeProvider.Attributes.References,
            _references.ToArray());
    }

    public GoAppResourceDefinitionBuilder MountVolume(
        IResourceDefinitionBuilder volume,
        string targetPath,
        bool readOnly = false)
    {
        ArgumentNullException.ThrowIfNull(volume);

        return MountVolume(volume.EffectiveResourceId, targetPath, readOnly);
    }

    public GoAppResourceDefinitionBuilder MountVolume(
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

    public GoAppResourceDefinitionBuilder AddHealthCheck(
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

        return $"cloudshell-go-{normalizedName}:{FirstNonEmpty(tag, "dev")}";
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}

public static class GoAppResourceDefinitionBuilderExtensions
{
    public static GoAppResourceDefinitionBuilder WithServiceDiscovery(
        this GoAppResourceDefinitionBuilder builder,
        bool enabled = true)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return enabled
            ? builder.WithServiceDiscoveryName(builder.Name)
            : builder;
    }

    public static GoAppResourceDefinitionBuilder WithHttpHealthCheck(
        this GoAppResourceDefinitionBuilder builder,
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

    public static GoAppResourceDefinitionBuilder WithHttpLivenessCheck(
        this GoAppResourceDefinitionBuilder builder,
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

    public static GoAppResourceDefinitionBuilder WithHttpProbe(
        this GoAppResourceDefinitionBuilder builder,
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

    public static GoAppResourceDefinitionBuilder AddGoApp(
        this ResourceGraphBuilder graph,
        string name,
        string projectPath,
        string packagePath = ".")
    {
        ArgumentNullException.ThrowIfNull(graph);

        graph.AddResourceTypeDefinition(new GoAppResourceTypeProvider().TypeDefinition);
        graph.AddResourceCapabilityAttributeProvider(new EnvironmentVariablesCapabilityProvider());
        graph.AddResourceCapabilityAttributeProvider(new VolumeConsumerCapabilityProvider());

        var builder = new GoAppResourceDefinitionBuilder(name)
            .WithProjectPath(projectPath)
            .WithCommand("go")
            .WithPackagePath(packagePath)
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

public sealed record GoAppEnvironmentVariableValue(
    [property: System.Text.Json.Serialization.JsonConverter(
        typeof(ApplicationEnvironmentVariableValueJsonConverter))]
    string? Value = null,
    ResourceConfigurationSettingReference? ConfigurationSettingRef = null,
    ResourceSecretReference? SecretRef = null);
