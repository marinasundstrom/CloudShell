using CloudShell.Abstractions.ResourceManager;
using CloudShell.Abstractions.Logs;
using ResourceOrchestratorSessionAffinityMode = CloudShell.Abstractions.ResourceManager.ResourceOrchestratorSessionAffinityMode;

namespace CloudShell.ControlPlane.Providers;

public sealed class ContainerApplicationResourceDefinitionBuilder(string name) :
    ResourceDefinitionBuilder<ContainerApplicationResourceDefinitionBuilder>(name)
{
    private readonly List<NetworkingEndpointRequestValue> _endpointRequests = [];
    private readonly List<VolumeMountDefinition> _volumeMounts = [];
    private readonly List<ResourceHealthCheckDefinition> _healthChecks = [];

    protected override ResourceTypeId TypeId =>
        ContainerApplicationResourceTypeProvider.ResourceTypeId;

    protected override string? ProviderId =>
        ContainerApplicationResourceTypeProvider.ProviderId;

    public ContainerApplicationResourceDefinitionBuilder WithImage(string image) =>
        SetScalarAttribute(ContainerApplicationResourceTypeProvider.Attributes.ContainerImage, image);

    public ContainerApplicationResourceDefinitionBuilder WithRegistry(string registry) =>
        SetScalarAttribute(ContainerApplicationResourceTypeProvider.Attributes.ContainerRegistry, registry);

    public ContainerApplicationResourceDefinitionBuilder WithProjectPath(string projectPath) =>
        SetScalarAttribute(ResourceAttributeId.Create(ResourceAttributeNames.ProjectPath), projectPath);

    public ContainerApplicationResourceDefinitionBuilder WithContainerBuild(
        string buildContext,
        string? dockerfile = null)
    {
        SetScalarAttribute(ContainerApplicationResourceTypeProvider.Attributes.ContainerBuildContext, buildContext);

        if (!string.IsNullOrWhiteSpace(dockerfile))
        {
            SetScalarAttribute(ContainerApplicationResourceTypeProvider.Attributes.ContainerDockerfile, dockerfile);
        }

        return this;
    }

    public ContainerApplicationResourceDefinitionBuilder WithReplicas(long replicas) =>
        SetScalarAttribute(ContainerApplicationResourceTypeProvider.Attributes.ContainerReplicas, replicas);

    public ContainerApplicationResourceDefinitionBuilder WithSessionAffinity(
        ResourceOrchestratorSessionAffinityMode mode,
        string? cookieName = null,
        int? durationSeconds = null)
    {
        SetScalarAttribute(
            ContainerApplicationResourceTypeProvider.Attributes.RoutingSessionAffinityMode,
            mode.ToString());

        if (!string.IsNullOrWhiteSpace(cookieName))
        {
            SetScalarAttribute(
                ContainerApplicationResourceTypeProvider.Attributes.RoutingSessionAffinityCookieName,
                cookieName.Trim());
        }

        if (durationSeconds is { } seconds)
        {
            SetScalarAttribute(
                ContainerApplicationResourceTypeProvider.Attributes.RoutingSessionAffinityDurationSeconds,
                Math.Max(1, seconds));
        }

        return this;
    }

    public ContainerApplicationResourceDefinitionBuilder WithClientIpSessionAffinity() =>
        WithSessionAffinity(ResourceOrchestratorSessionAffinityMode.ClientIp);

    public ContainerApplicationResourceDefinitionBuilder WithCookieSessionAffinity(
        string cookieName = "CloudShellReplica",
        int? durationSeconds = null) =>
        WithSessionAffinity(
            ResourceOrchestratorSessionAffinityMode.Cookie,
            cookieName,
            durationSeconds);

    public ContainerApplicationResourceDefinitionBuilder WithoutSessionAffinity() =>
        WithSessionAffinity(ResourceOrchestratorSessionAffinityMode.None);

    public ContainerApplicationResourceDefinitionBuilder WithRuntimeMonitoring() =>
        this;

    public ContainerApplicationResourceDefinitionBuilder WithRuntimeLogSources() =>
        WithRuntimeLogSources(ResourceLogSourceDefinitionValues.PlainText);

    public ContainerApplicationResourceDefinitionBuilder WithRuntimeLogSources(LogFormat format) =>
        WithRuntimeLogSources(ToResourceLogFormat(format));

    public ContainerApplicationResourceDefinitionBuilder WithRuntimeLogSources(string format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);

        return SetObjectAttribute(
            ResourceLogSourceAttributeIds.LogSources,
            ResourceLogSourceDefinitionSet.DefaultConsole(format.Trim()));
    }

    public ContainerApplicationResourceDefinitionBuilder UseContainerHost(
        IResourceDefinitionBuilder host,
        ResourceTypeId? typeId = null)
    {
        ArgumentNullException.ThrowIfNull(host);

        return UseContainerHost(host.EffectiveResourceId, typeId);
    }

    public ContainerApplicationResourceDefinitionBuilder UseContainerHost(
        string hostResourceId,
        ResourceTypeId? typeId = null)
    {
        RemoveContainerHostDependencies();
        return AddDependency(ResourceReference.DependsOnResourceId(
            hostResourceId,
            typeId ?? ContainerHostResourceTypeProvider.ResourceTypeId));
    }

    public ContainerApplicationResourceDefinitionBuilder UseDockerHost(
        IResourceDefinitionBuilder host)
    {
        ArgumentNullException.ThrowIfNull(host);

        return UseDockerHost(host.EffectiveResourceId);
    }

    public ContainerApplicationResourceDefinitionBuilder UseDockerHost(string hostResourceId) =>
        UseContainerHost(hostResourceId, DockerHostResourceTypeProvider.ResourceTypeId);

    public ContainerApplicationResourceDefinitionBuilder MountVolume(
        IResourceDefinitionBuilder volume,
        string targetPath,
        bool readOnly = false)
    {
        ArgumentNullException.ThrowIfNull(volume);

        return MountVolume(volume.EffectiveResourceId, targetPath, readOnly);
    }

    public ContainerApplicationResourceDefinitionBuilder MountVolume(
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

    public ContainerApplicationResourceDefinitionBuilder AddEndpointRequest(
        string name,
        string protocol,
        int targetPort,
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
            ContainerApplicationResourceTypeProvider.Attributes.EndpointRequests,
            _endpointRequests.ToArray());
    }

    public ContainerApplicationResourceDefinitionBuilder WithEndpoint(
        string name,
        int targetPort,
        int? port = null,
        string protocol = "tcp",
        string? host = null,
        string exposure = "Local",
        string? ipAddress = null,
        IResourceDefinitionBuilder? network = null,
        string? assignment = null) =>
        AddEndpointRequest(
            name,
            protocol,
            targetPort,
            host,
            port,
            exposure,
            ipAddress,
            network,
            assignment);

    public ContainerApplicationResourceDefinitionBuilder WithHttpEndpoint(
        int targetPort = 80,
        int? port = null,
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

    public ContainerApplicationResourceDefinitionBuilder WithHttpsEndpoint(
        int targetPort = 443,
        int? port = null,
        string name = "https",
        string? host = null,
        string exposure = "Local",
        string? ipAddress = null,
        IResourceDefinitionBuilder? network = null,
        string? assignment = null) =>
        AddEndpointRequest(
            name,
            "https",
            targetPort,
            host,
            port,
            exposure,
            ipAddress,
            network,
            assignment);

    public ContainerApplicationResourceDefinitionBuilder WithTcpEndpoint(
        string name,
        int targetPort,
        int? port = null,
        string? host = null,
        string exposure = "Local",
        string? ipAddress = null,
        IResourceDefinitionBuilder? network = null,
        string? assignment = null) =>
        AddEndpointRequest(
            name,
            "tcp",
            targetPort,
            host,
            port,
            exposure,
            ipAddress,
            network,
            assignment);

    public ContainerApplicationResourceDefinitionBuilder AddHealthCheck(
        ResourceHealthCheckDefinition check)
    {
        ArgumentNullException.ThrowIfNull(check);

        _healthChecks.Add(check);
        return SetObjectAttribute(
            ResourceHealthCheckAttributeIds.HealthChecks,
            new ResourceHealthCheckDefinitionSet(_healthChecks.ToArray()));
    }

    private static string ToResourceLogFormat(LogFormat format) =>
        format switch
        {
            LogFormat.PlainText => ResourceLogSourceDefinitionValues.PlainText,
            LogFormat.JsonConsole => ResourceLogSourceDefinitionValues.JsonConsole,
            _ => format.ToString()
        };

    protected override void OnBeforeBuild()
    {
        if (HasContainerHostDependency() ||
            ResourceGraph is not { } graph)
        {
            return;
        }

        UseContainerHost(graph.GetContainerHost());
    }

    private bool HasContainerHostDependency() =>
        Dependencies.Any(IsContainerHostDependency);

    private ContainerApplicationResourceDefinitionBuilder RemoveContainerHostDependencies() =>
        RemoveDependencies(IsContainerHostDependency);

    private static bool IsContainerHostDependency(ResourceReference reference) =>
        reference.TypeId is { } typeId &&
        ContainerApplicationResourceTypeProvider.IsContainerHostResourceType(typeId);
}

public static class ContainerApplicationResourceDefinitionBuilderExtensions
{
    public static ContainerApplicationResourceDefinitionBuilder WithHttpHealthCheck(
        this ContainerApplicationResourceDefinitionBuilder builder,
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

    public static ContainerApplicationResourceDefinitionBuilder WithHttpLivenessCheck(
        this ContainerApplicationResourceDefinitionBuilder builder,
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

    public static ContainerApplicationResourceDefinitionBuilder WithHttpProbe(
        this ContainerApplicationResourceDefinitionBuilder builder,
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

    public static ContainerApplicationResourceDefinitionBuilder AddContainerApplication(
        this ResourceGraphBuilder graph,
        string name)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var builder = new ContainerApplicationResourceDefinitionBuilder(name)
            .WithRuntimeMonitoring()
            .WithRuntimeLogSources();
        graph.Add(builder);
        return builder;
    }

    private static int? ToMilliseconds(TimeSpan? value) =>
        value is null ? null : Math.Max(1, (int)Math.Ceiling(value.Value.TotalMilliseconds));

    private static int? ToSeconds(TimeSpan? value) =>
        value is null ? null : Math.Max(1, (int)Math.Ceiling(value.Value.TotalSeconds));
}
