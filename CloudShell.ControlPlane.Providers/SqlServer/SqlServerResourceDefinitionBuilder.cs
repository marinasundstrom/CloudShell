namespace CloudShell.ControlPlane.Providers;

public sealed class SqlServerResourceDefinitionBuilder(string name) :
    ResourceDefinitionBuilder<SqlServerResourceDefinitionBuilder>(name)
{
    private readonly List<SqlServerDatabaseDefinition> _databases = [];
    private readonly List<NetworkingEndpointRequestValue> _endpointRequests = [];
    private readonly List<VolumeMountDefinition> _volumeMounts = [];

    protected override ResourceTypeId TypeId =>
        SqlServerResourceTypeProvider.ResourceTypeId;

    protected override string? ProviderId =>
        SqlServerResourceTypeProvider.ProviderId;

    public SqlServerResourceDefinitionBuilder WithVersion(string version) =>
        SetScalarAttribute(SqlServerResourceTypeProvider.Attributes.Version, version);

    public SqlServerResourceDefinitionBuilder WithEdition(string edition) =>
        SetScalarAttribute(SqlServerResourceTypeProvider.Attributes.Edition, edition);

    public SqlServerResourceDefinitionBuilder AddEndpointRequest(
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
            SqlServerResourceTypeProvider.Attributes.EndpointRequests,
            _endpointRequests.ToArray());
    }

    public SqlServerResourceDefinitionBuilder WithEndpoint(
        string name,
        int targetPort,
        int? port = null,
        string protocol = "tcp",
        string? host = null,
        string exposure = "Local") =>
        AddEndpointRequest(name, protocol, targetPort, host, port, exposure);

    public SqlServerResourceDefinitionBuilder WithTcpEndpoint(
        string name = "tds",
        int targetPort = 1433,
        int? port = null,
        string? host = null,
        string exposure = "Local") =>
        AddEndpointRequest(name, "tcp", targetPort, host, port, exposure);

    public SqlServerResourceDefinitionBuilder UseContainerHost(
        IResourceDefinitionBuilder containerHost,
        ResourceTypeId? typeId = null)
    {
        ArgumentNullException.ThrowIfNull(containerHost);

        return UseContainerHost(containerHost.EffectiveResourceId, typeId);
    }

    public SqlServerResourceDefinitionBuilder UseContainerHost(
        string containerHostResourceId,
        ResourceTypeId? typeId = null) =>
        AddDependency(ResourceReference.DependsOnResourceId(
            containerHostResourceId,
            typeId ?? ContainerHostResourceTypeProvider.ResourceTypeId));

    public SqlServerResourceDefinitionBuilder MountVolume(
        IResourceDefinitionBuilder volume,
        string targetPath,
        bool readOnly = false)
    {
        ArgumentNullException.ThrowIfNull(volume);

        return MountVolume(volume.EffectiveResourceId, targetPath, readOnly);
    }

    public SqlServerResourceDefinitionBuilder MountVolume(
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

    public SqlServerResourceDefinitionBuilder DeclareDatabase(
        string name,
        string? displayName = null,
        bool ensureCreated = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        _databases.Add(new(
            name.Trim(),
            string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim(),
            ensureCreated));
        return SetConfiguration(
            SqlServerResourceTypeProvider.ConfigurationSection,
            new SqlServerConfiguration(_databases.ToArray()));
    }
}

public static class SqlServerResourceDefinitionBuilderExtensions
{
    public static SqlServerResourceDefinitionBuilder AddSqlServer(
        this ResourceGraphBuilder graph,
        string name)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var builder = new SqlServerResourceDefinitionBuilder(name);
        graph.Add(builder);
        return builder;
    }
}
