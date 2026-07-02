namespace CloudShell.ControlPlane.Providers;

public sealed class JavaAppResourceDefinitionBuilder(string name) :
    ContainerizableResourceDefinitionBuilder<JavaAppResourceDefinitionBuilder>(
        name,
        JavaAppResourceTypeProvider.ResourceTypeId,
        JavaAppResourceTypeProvider.ProviderId)
{
    private readonly List<NetworkingEndpointRequestValue> _endpointRequests = [];
    private readonly Dictionary<string, JavaAppEnvironmentVariableValue> _environmentVariables =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ResourceReference> _references = [];
    private readonly List<VolumeMountDefinition> _volumeMounts = [];
    private readonly List<ResourceHealthCheckDefinition> _healthChecks = [];

    public JavaAppResourceDefinitionBuilder WithProjectPath(string projectPath) =>
        SetScalarAttribute(JavaAppResourceTypeProvider.Attributes.ProjectPath, projectPath);

    public JavaAppResourceDefinitionBuilder WithCommand(string command) =>
        SetScalarAttribute(JavaAppResourceTypeProvider.Attributes.Command, command);

    public JavaAppResourceDefinitionBuilder WithArtifactPath(string artifactPath) =>
        SetScalarAttribute(JavaAppResourceTypeProvider.Attributes.ArtifactPath, artifactPath);

    public JavaAppResourceDefinitionBuilder WithMainClass(string mainClass) =>
        SetScalarAttribute(JavaAppResourceTypeProvider.Attributes.MainClass, mainClass);

    public JavaAppResourceDefinitionBuilder WithClassPath(string classPath) =>
        SetScalarAttribute(JavaAppResourceTypeProvider.Attributes.ClassPath, classPath);

    public JavaAppResourceDefinitionBuilder WithJvmArguments(string arguments) =>
        SetScalarAttribute(JavaAppResourceTypeProvider.Attributes.JvmArguments, arguments);

    public JavaAppResourceDefinitionBuilder WithArguments(string arguments) =>
        SetScalarAttribute(JavaAppResourceTypeProvider.Attributes.Arguments, arguments);

    public JavaAppResourceDefinitionBuilder WithServiceDiscoveryName(string name) =>
        SetScalarAttribute(JavaAppResourceTypeProvider.Attributes.ServiceDiscoveryName, name);

    public JavaAppResourceDefinitionBuilder AsContainer(
        string? image = null,
        string? registry = null,
        string? tag = null,
        string? buildContext = null,
        string? dockerfile = null)
    {
        var effectiveBuildContext = FirstNonEmpty(
            buildContext,
            Attributes.TryGetValue(
                JavaAppResourceTypeProvider.Attributes.ProjectPath,
                out var projectPath)
                    ? projectPath.StringValue
                    : null);
        return ProjectAsContainerApplication(
            FirstNonEmpty(image, CreateDefaultContainerImage(Name, tag))!,
            registry,
            effectiveBuildContext,
            dockerfile,
            JavaAppResourceTypeProvider.Attributes.EndpointRequests,
            _endpointRequests);
    }

    public JavaAppResourceDefinitionBuilder WithReplicas(long replicas) =>
        SetContainerReplicas(replicas);

    public JavaAppResourceDefinitionBuilder WithRuntimeMonitoring() =>
        this;

    public JavaAppResourceDefinitionBuilder WithDefaultConsoleLogSource() =>
        SetObjectAttribute(
            ResourceLogSourceAttributeIds.LogSources,
            ResourceLogSourceDefinitionSet.DefaultConsole());

    public JavaAppResourceDefinitionBuilder AddEndpointRequest(
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
                : JavaAppResourceTypeProvider.Attributes.EndpointRequests,
            _endpointRequests.ToArray());
    }

    public JavaAppResourceDefinitionBuilder WithHttpEndpoint(
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

    public JavaAppResourceDefinitionBuilder WithEnvironmentVariable(
        string name,
        string? value = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        _environmentVariables[name.Trim()] = new(
            string.IsNullOrWhiteSpace(value) ? null : value.Trim());
        return SetObjectAttribute(
            JavaAppResourceTypeProvider.Attributes.EnvironmentVariables,
            _environmentVariables);
    }

    public JavaAppResourceDefinitionBuilder WithEnvironmentVariable(
        string name,
        ResourceConfigurationEntryReference configurationEntry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configurationEntry);

        _environmentVariables[name.Trim()] = new(
            ConfigurationEntryRef: configurationEntry);
        return SetObjectAttribute(
            JavaAppResourceTypeProvider.Attributes.EnvironmentVariables,
            _environmentVariables);
    }

    public JavaAppResourceDefinitionBuilder WithEnvironmentVariable(
        string name,
        ResourceSecretReference secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(secret);

        _environmentVariables[name.Trim()] = new(
            SecretRef: secret);
        return SetObjectAttribute(
            JavaAppResourceTypeProvider.Attributes.EnvironmentVariables,
            _environmentVariables);
    }

    public JavaAppResourceDefinitionBuilder WithReference(
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

    public JavaAppResourceDefinitionBuilder WithReference(
        string resourceId,
        ResourceTypeId? typeId = null,
        string? providerId = null)
    {
        _references.Add(ResourceReference.ReferenceResourceId(resourceId, typeId, providerId));
        return SetObjectAttribute(
            JavaAppResourceTypeProvider.Attributes.References,
            _references.ToArray());
    }

    public JavaAppResourceDefinitionBuilder MountVolume(
        IResourceDefinitionBuilder volume,
        string targetPath,
        bool readOnly = false)
    {
        ArgumentNullException.ThrowIfNull(volume);

        return MountVolume(volume.EffectiveResourceId, targetPath, readOnly);
    }

    public JavaAppResourceDefinitionBuilder MountVolume(
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

    public JavaAppResourceDefinitionBuilder AddHealthCheck(
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

        return $"cloudshell-java-{normalizedName}:{FirstNonEmpty(tag, "dev")}";
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}

public static class JavaAppResourceDefinitionBuilderExtensions
{
    public static JavaAppResourceDefinitionBuilder WithServiceDiscovery(
        this JavaAppResourceDefinitionBuilder builder,
        bool enabled = true)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return enabled
            ? builder.WithServiceDiscoveryName(builder.Name)
            : builder;
    }

    public static JavaAppResourceDefinitionBuilder WithHttpHealthCheck(
        this JavaAppResourceDefinitionBuilder builder,
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

    public static JavaAppResourceDefinitionBuilder WithHttpLivenessCheck(
        this JavaAppResourceDefinitionBuilder builder,
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

    public static JavaAppResourceDefinitionBuilder WithHttpProbe(
        this JavaAppResourceDefinitionBuilder builder,
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

    public static JavaAppResourceDefinitionBuilder AddJavaApp(
        this ResourceGraphBuilder graph,
        string name,
        string projectPath,
        string artifactPath)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var builder = new JavaAppResourceDefinitionBuilder(name)
            .WithProjectPath(projectPath)
            .WithCommand("java")
            .WithArtifactPath(artifactPath)
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

public sealed record JavaAppEnvironmentVariableValue(
    string? Value = null,
    ResourceConfigurationEntryReference? ConfigurationEntryRef = null,
    ResourceSecretReference? SecretRef = null);
