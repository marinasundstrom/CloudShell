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

    public ContainerApplicationResourceDefinitionBuilder WithReplicas(long replicas) =>
        SetScalarAttribute(ContainerApplicationResourceTypeProvider.Attributes.ContainerReplicas, replicas);

    public ContainerApplicationResourceDefinitionBuilder WithRuntimeMonitoring() =>
        DeclareCapability(ResourceCommonCapabilityIds.Monitoring);

    public ContainerApplicationResourceDefinitionBuilder WithRuntimeLogSources() =>
        DeclareCapability(ResourceLogSourceCapabilityIds.LogSources);

    public ContainerApplicationResourceDefinitionBuilder UseContainerHost(
        IResourceDefinitionBuilder host,
        ResourceTypeId? typeId = null)
    {
        ArgumentNullException.ThrowIfNull(host);

        return UseContainerHost(host.EffectiveResourceId, typeId);
    }

    public ContainerApplicationResourceDefinitionBuilder UseContainerHost(
        string hostResourceId,
        ResourceTypeId? typeId = null) =>
        AddDependency(ResourceReference.DependsOnResourceId(
            hostResourceId,
            typeId ?? ContainerHostResourceTypeProvider.ResourceTypeId));

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
        return SetCapability(
            VolumeConsumerCapabilityProvider.CapabilityIdValue,
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

        _endpointRequests.Add(new NetworkingEndpointRequestValue(
            name.Trim(),
            protocol.Trim(),
            TargetPort: targetPort,
            Host: string.IsNullOrWhiteSpace(host) ? null : host.Trim(),
            Port: port,
            IpAddress: string.IsNullOrWhiteSpace(ipAddress) ? null : ipAddress.Trim(),
            Exposure: string.IsNullOrWhiteSpace(exposure) ? null : exposure.Trim(),
            Assignment: string.IsNullOrWhiteSpace(assignment) ? null : assignment.Trim(),
            Network: network is null
                ? null
                : ResourceReference.ReferenceResourceId(
                    network.EffectiveResourceId,
                    network.ResourceTypeId,
                    network.ResourceProviderId)));
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
        return SetCapability(
            ResourceHealthCheckCapabilityIds.HealthChecks,
            new ResourceHealthCheckDefinitionSet(_healthChecks.ToArray()));
    }
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
        this ResourceDefinitionGraphBuilder graph,
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
