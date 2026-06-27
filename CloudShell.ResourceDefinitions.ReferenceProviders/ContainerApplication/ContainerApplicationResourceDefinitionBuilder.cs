namespace CloudShell.ResourceDefinitions.ReferenceProviders;

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
            ContainerApplicationResourceTypeProvider.Attributes.EndpointRequests,
            _endpointRequests.ToArray());
    }

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
    public static ContainerApplicationResourceDefinitionBuilder AddContainerApplication(
        this ResourceDefinitionGraphBuilder graph,
        string name)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var builder = new ContainerApplicationResourceDefinitionBuilder(name);
        graph.Add(builder);
        return builder;
    }
}
