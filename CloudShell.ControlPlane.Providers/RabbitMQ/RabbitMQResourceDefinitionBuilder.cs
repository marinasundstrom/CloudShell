namespace CloudShell.ControlPlane.Providers;

public sealed class RabbitMQResourceDefinitionBuilder(string name) :
    ResourceDefinitionBuilder<RabbitMQResourceDefinitionBuilder>(name)
{
    private readonly List<NetworkingEndpointRequestValue> _endpointRequests = [];
    private readonly List<VolumeMountDefinition> _volumeMounts = [];

    protected override ResourceTypeId TypeId =>
        RabbitMQResourceTypeProvider.ResourceTypeId;

    protected override string? ProviderId =>
        RabbitMQResourceTypeProvider.ProviderId;

    public RabbitMQResourceDefinitionBuilder WithVersion(string version) =>
        SetScalarAttribute(RabbitMQResourceTypeProvider.Attributes.Version, version);

    public RabbitMQResourceDefinitionBuilder WithManagementUi(bool enabled = true) =>
        SetScalarAttribute(RabbitMQResourceTypeProvider.Attributes.ManagementUi, enabled);

    public RabbitMQResourceDefinitionBuilder WithUser(
        string username,
        string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        RemoveAttribute(RabbitMQResourceTypeProvider.Attributes.UserManaged);
        SetScalarAttribute(RabbitMQResourceTypeProvider.Attributes.UserName, username);
        return SetScalarAttribute(RabbitMQResourceTypeProvider.Attributes.UserPassword, password);
    }

    public RabbitMQResourceDefinitionBuilder WithCloudShellManagedUser(bool managed = true)
    {
        if (managed)
        {
            RemoveAttribute(RabbitMQResourceTypeProvider.Attributes.UserName);
            RemoveAttribute(RabbitMQResourceTypeProvider.Attributes.UserPassword);
        }

        return SetScalarAttribute(RabbitMQResourceTypeProvider.Attributes.UserManaged, managed);
    }

    public RabbitMQResourceDefinitionBuilder WithVirtualHost(string virtualHost) =>
        SetScalarAttribute(RabbitMQResourceTypeProvider.Attributes.VirtualHost, virtualHost);

    public RabbitMQResourceDefinitionBuilder WithDefaultContainerLogSource() =>
        SetObjectAttribute(
            ResourceLogSourceAttributeIds.LogSources,
            new ResourceLogSourceDefinitionSet(
                [ResourceLogSourceDefinition.DefaultContainerConsole()]));

    public RabbitMQResourceDefinitionBuilder AddEndpointRequest(
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
            RabbitMQResourceTypeProvider.Attributes.EndpointRequests,
            _endpointRequests.ToArray());
    }

    public RabbitMQResourceDefinitionBuilder WithAmqpEndpoint(
        string name = "amqp",
        int targetPort = 5672,
        int? port = null,
        string? host = null,
        string exposure = "Local",
        string? ipAddress = null,
        IResourceDefinitionBuilder? network = null,
        string? assignment = null) =>
        AddEndpointRequest(name, "tcp", targetPort, host, port, exposure, ipAddress, network, assignment);

    public RabbitMQResourceDefinitionBuilder WithManagementEndpoint(
        string name = "management",
        int targetPort = 15672,
        int? port = null,
        string? host = null,
        string exposure = "Local",
        string? ipAddress = null,
        IResourceDefinitionBuilder? network = null,
        string? assignment = null) =>
        AddEndpointRequest(name, "http", targetPort, host, port, exposure, ipAddress, network, assignment);

    public RabbitMQResourceDefinitionBuilder UseContainerHost(
        IResourceDefinitionBuilder containerHost,
        ResourceTypeId? typeId = null)
    {
        ArgumentNullException.ThrowIfNull(containerHost);

        return UseContainerHost(containerHost.EffectiveResourceId, typeId);
    }

    public RabbitMQResourceDefinitionBuilder UseContainerHost(
        string containerHostResourceId,
        ResourceTypeId? typeId = null)
    {
        RemoveContainerHostDependencies();
        return AddDependency(ResourceReference.DependsOnResourceId(
            containerHostResourceId,
            typeId ?? ContainerHostResourceTypeProvider.ResourceTypeId));
    }

    public RabbitMQResourceDefinitionBuilder MountVolume(
        IResourceDefinitionBuilder volume,
        string targetPath,
        bool readOnly = false)
    {
        ArgumentNullException.ThrowIfNull(volume);

        return MountVolume(volume.EffectiveResourceId, targetPath, readOnly);
    }

    public RabbitMQResourceDefinitionBuilder MountVolume(
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

    private RabbitMQResourceDefinitionBuilder RemoveContainerHostDependencies() =>
        RemoveDependencies(IsContainerHostDependency);

    private static bool IsContainerHostDependency(ResourceReference reference) =>
        reference.TypeId is { } typeId &&
        RabbitMQResourceTypeProvider.IsContainerHostResourceType(typeId);
}

public static class RabbitMQResourceDefinitionBuilderExtensions
{
    public static RabbitMQResourceDefinitionBuilder AddRabbitMQ(
        this ResourceGraphBuilder graph,
        string name)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var builder = new RabbitMQResourceDefinitionBuilder(name)
            .WithDefaultContainerLogSource();
        graph.Add(builder);
        return builder;
    }
}
