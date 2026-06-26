namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class AspNetCoreProjectResourceDefinitionBuilder(string name) :
    ResourceDefinitionBuilder<AspNetCoreProjectResourceDefinitionBuilder>(name)
{
    private readonly List<NetworkingEndpointRequestValue> _endpointRequests = [];
    private readonly List<AspNetCoreProjectEnvironmentVariableValue> _environmentVariables = [];
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

        _environmentVariables.Add(new(
            name.Trim(),
            string.IsNullOrWhiteSpace(value) ? null : value.Trim()));
        return SetObjectAttribute(
            AspNetCoreProjectResourceTypeProvider.Attributes.EnvironmentVariables,
            _environmentVariables.ToArray());
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
    public static AspNetCoreProjectResourceDefinitionBuilder AddAspNetCoreProject(
        this ResourceDefinitionGraphBuilder graph,
        string name,
        string projectPath)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var builder = new AspNetCoreProjectResourceDefinitionBuilder(name)
            .WithProjectPath(projectPath);
        graph.Add(builder);
        return builder;
    }
}
