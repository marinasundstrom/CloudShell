using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.ControlPlane.ResourceManager;

public static class PlatformResourceDeclarationExtensions
{
    public static INetworkResourceBuilder AddNetwork(
        this IResourceDeclarationBuilder builder,
        string id,
        string name,
        bool isDefault = false)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var definition = new NetworkResourceDefinition(id, name, isDefault);
        var declared = new DeclaredNetworkResource(definition);
        builder.Services
            .GetOrAddPlatformResourceOptions()
            .DeclaredNetworks
            .Add(declared);

        var resource = builder.Declare(
            PlatformResourceProvider.ProviderId,
            definition.Id,
            onChanged: declaration =>
            {
                declared.Persist = declaration.Persistence == ResourceDeclarationPersistence.Persisted;
                declared.OverwritePersistedState = declaration.OverwritePersistedState;
            });

        return new NetworkResourceBuilder(resource, declared);
    }

    public static INetworkResourceBuilder AddVirtualNetwork(
        this IResourceDeclarationBuilder builder,
        string id,
        string name,
        bool isDefault = false)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var definition = new NetworkResourceDefinition(
            id,
            name,
            isDefault,
            Kind: NetworkResourceKind.Virtual);
        var declared = new DeclaredNetworkResource(definition);
        builder.Services
            .GetOrAddPlatformResourceOptions()
            .DeclaredNetworks
            .Add(declared);

        var resource = builder.Declare(
            PlatformResourceProvider.ProviderId,
            definition.Id,
            onChanged: declaration =>
            {
                declared.Persist = declaration.Persistence == ResourceDeclarationPersistence.Persisted;
                declared.OverwritePersistedState = declaration.OverwritePersistedState;
            });

        return new NetworkResourceBuilder(resource, declared);
    }

    public static IServiceResourceBuilder AddService(
        this IResourceDeclarationBuilder builder,
        string id,
        string name)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var definition = new ServiceResourceDefinition(id, name, [], [], []);
        var declared = new DeclaredServiceResource(definition);
        builder.Services
            .GetOrAddPlatformResourceOptions()
            .DeclaredServices
            .Add(declared);

        var resource = builder.Declare(
            PlatformResourceProvider.ProviderId,
            definition.Id,
            onChanged: declaration =>
            {
                declared.Persist = declaration.Persistence == ResourceDeclarationPersistence.Persisted;
                declared.OverwritePersistedState = declaration.OverwritePersistedState;
                declared.Definition = declared.Definition with
                {
                    NetworkIds = declared.Definition.NetworkIds,
                    Targets = declared.Definition.Targets,
                    Ports = declared.Definition.Ports
                };
            });

        return new ServiceResourceBuilder(resource, declared);
    }

    public static PlatformResourceOptions GetOrAddPlatformResourceOptions(
        this IServiceCollection services)
    {
        var options = services
            .Where(descriptor => descriptor.ServiceType == typeof(PlatformResourceOptions))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<PlatformResourceOptions>()
            .SingleOrDefault();

        if (options is not null)
        {
            return options;
        }

        options = new PlatformResourceOptions();
        services.AddSingleton(options);
        return options;
    }
}

public interface INetworkResourceBuilder : IResourceBuilder
{
    INetworkResourceBuilder AsDefault(bool isDefault = true);

    ResourceEndpointReference AddTcpEndpoint(
        string host,
        int? port = null,
        string name = "tcp",
        ResourceExposureScope exposure = ResourceExposureScope.Local);

    ResourceEndpointReference AddHttpEndpoint(
        string host,
        int? port = null,
        string name = "http",
        ResourceExposureScope exposure = ResourceExposureScope.Local);

    ResourceEndpointReference RequestTcpEndpoint(
        string name,
        string host = "localhost",
        int? port = null,
        ResourceExposureScope exposure = ResourceExposureScope.Local);

    ResourceEndpointReference RequestHttpEndpoint(
        string name,
        string host = "localhost",
        int? port = null,
        ResourceExposureScope exposure = ResourceExposureScope.Local);

    INetworkResourceBuilder MapEndpoint(
        ResourceEndpointReference source,
        ResourceEndpointReference target,
        string? id = null,
        string? name = null,
        string? providerResourceId = null);

    INetworkResourceBuilder MapEndpoint(
        ResourceEndpointReference source,
        ResourceEndpointReference target,
        IResourceBuilder provider,
        string? id = null,
        string? name = null);

    new INetworkResourceBuilder DependsOn(string resourceId);

    new INetworkResourceBuilder DependsOn(IResourceBuilder resource);

    new INetworkResourceBuilder DependsOn(IEnumerable<string> resourceIds);

    new INetworkResourceBuilder DependsOn(IEnumerable<IResourceBuilder> resources);

    new INetworkResourceBuilder WithResourceGroup(string? resourceGroupId);

    new INetworkResourceBuilder WithParent(string? parentResourceId);

    new INetworkResourceBuilder WithParent(IResourceBuilder resource);

    new INetworkResourceBuilder WithReference(string resourceId);

    new INetworkResourceBuilder WithReference(IResourceBuilder resource);

    new INetworkResourceBuilder WithReferences(IEnumerable<string> resourceIds);

    new INetworkResourceBuilder Persist(bool overwrite = false);
}

public interface IServiceResourceBuilder : IResourceBuilder
{
    IServiceResourceBuilder Targets(IResourceBuilder resource, int weight = 100);

    IServiceResourceBuilder Targets(string resourceId, int weight = 100);

    IServiceResourceBuilder Targets(IEnumerable<IResourceBuilder> resources);

    IServiceResourceBuilder WithPort(
        string name,
        int targetPort,
        int? port = null,
        string protocol = "tcp",
        ResourceExposureScope exposure = ResourceExposureScope.Local);

    IServiceResourceBuilder ExposePublic(
        string name = "public",
        int targetPort = 80,
        int? port = null,
        string protocol = "tcp");

    IServiceResourceBuilder WithHttpHealthCheck(
        string path,
        string? endpointName = null,
        string name = "health",
        TimeSpan? timeout = null);

    IServiceResourceBuilder WithHttpProbe(
        ResourceProbeType type,
        string path,
        string? endpointName = null,
        string? name = null,
        TimeSpan? timeout = null);

    IServiceResourceBuilder WithNetwork(INetworkResourceBuilder network);

    IServiceResourceBuilder WithNetwork(string networkId);

    new IServiceResourceBuilder DependsOn(string resourceId);

    new IServiceResourceBuilder DependsOn(IResourceBuilder resource);

    new IServiceResourceBuilder DependsOn(IEnumerable<string> resourceIds);

    new IServiceResourceBuilder DependsOn(IEnumerable<IResourceBuilder> resources);

    new IServiceResourceBuilder WithResourceGroup(string? resourceGroupId);

    new IServiceResourceBuilder WithParent(string? parentResourceId);

    new IServiceResourceBuilder WithParent(IResourceBuilder resource);

    new IServiceResourceBuilder WithReference(string resourceId);

    new IServiceResourceBuilder WithReference(IResourceBuilder resource);

    new IServiceResourceBuilder WithReferences(IEnumerable<string> resourceIds);

    new IServiceResourceBuilder Persist(bool overwrite = false);
}

internal sealed class NetworkResourceBuilder(
    IResourceBuilder inner,
    DeclaredNetworkResource declared) : INetworkResourceBuilder
{
    public ICloudShellBuilder CloudShellBuilder => inner.CloudShellBuilder;

    public string ResourceId => inner.ResourceId;

    public INetworkResourceBuilder AsDefault(bool isDefault = true)
    {
        declared.Definition = declared.Definition with { IsDefault = isDefault };
        return this;
    }

    public ResourceEndpointReference AddTcpEndpoint(
        string host,
        int? port = null,
        string name = "tcp",
        ResourceExposureScope exposure = ResourceExposureScope.Local) =>
        AddEndpoint(name, ResourceEndpointProtocol.Tcp, ResourceEndpointAssignment.Manual, host, port, exposure);

    public ResourceEndpointReference AddHttpEndpoint(
        string host,
        int? port = null,
        string name = "http",
        ResourceExposureScope exposure = ResourceExposureScope.Local) =>
        AddEndpoint(name, ResourceEndpointProtocol.Http, ResourceEndpointAssignment.Manual, host, port, exposure);

    public ResourceEndpointReference RequestTcpEndpoint(
        string name,
        string host = "localhost",
        int? port = null,
        ResourceExposureScope exposure = ResourceExposureScope.Local) =>
        AddEndpoint(name, ResourceEndpointProtocol.Tcp, ResourceEndpointAssignment.Auto, host, port, exposure);

    public ResourceEndpointReference RequestHttpEndpoint(
        string name,
        string host = "localhost",
        int? port = null,
        ResourceExposureScope exposure = ResourceExposureScope.Local) =>
        AddEndpoint(name, ResourceEndpointProtocol.Http, ResourceEndpointAssignment.Auto, host, port, exposure);

    public INetworkResourceBuilder MapEndpoint(
        ResourceEndpointReference source,
        ResourceEndpointReference target,
        string? id = null,
        string? name = null,
        string? providerResourceId = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        var mappingId = string.IsNullOrWhiteSpace(id)
            ? $"{ResourceId}:mapping:{source.EndpointName}:{target.ResourceId}:{target.EndpointName}"
            : id.Trim();
        var mappingName = string.IsNullOrWhiteSpace(name)
            ? $"{source.EndpointName} to {target.EndpointName}"
            : name.Trim();
        var providerId = string.IsNullOrWhiteSpace(providerResourceId)
            ? ResourceId
            : providerResourceId.Trim();
        declared.Definition = declared.Definition with
        {
            EndpointMappings = declared.Definition.NetworkEndpointMappings
                .Where(mapping => !string.Equals(mapping.Id, mappingId, StringComparison.OrdinalIgnoreCase))
                .Append(new ResourceEndpointMappingDefinition(
                    mappingId,
                    mappingName,
                    source,
                    target,
                    NetworkResourceId: ResourceId,
                    ProviderResourceId: providerId))
                .ToArray()
        };
        inner.DependsOn(target.ResourceId);
        if (!string.Equals(source.ResourceId, ResourceId, StringComparison.OrdinalIgnoreCase))
        {
            inner.DependsOn(source.ResourceId);
        }

        if (!string.Equals(providerId, ResourceId, StringComparison.OrdinalIgnoreCase))
        {
            inner.DependsOn(providerId);
        }

        return this;
    }

    public INetworkResourceBuilder MapEndpoint(
        ResourceEndpointReference source,
        ResourceEndpointReference target,
        IResourceBuilder provider,
        string? id = null,
        string? name = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return MapEndpoint(source, target, id, name, provider.ResourceId);
    }

    private ResourceEndpointReference AddEndpoint(
        string name,
        ResourceEndpointProtocol protocol,
        ResourceEndpointAssignment assignment,
        string host,
        int? port,
        ResourceExposureScope exposure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        var normalizedName = name.Trim();
        declared.Definition = declared.Definition with
        {
            Endpoints = declared.Definition.NetworkEndpoints
                .Where(endpoint => !string.Equals(endpoint.Name, normalizedName, StringComparison.OrdinalIgnoreCase))
                .Append(new ResourceEndpointRequest(
                    normalizedName,
                    protocol,
                    Host: host.Trim(),
                    Port: port,
                    Exposure: exposure,
                    Assignment: assignment,
                    NetworkResourceId: ResourceId))
                .ToArray()
        };
        return new ResourceEndpointReference(ResourceId, normalizedName);
    }

    public INetworkResourceBuilder WithResourceGroup(string? resourceGroupId)
    {
        inner.WithResourceGroup(resourceGroupId);
        return this;
    }

    public INetworkResourceBuilder WithParent(string? parentResourceId)
    {
        inner.WithParent(parentResourceId);
        return this;
    }

    public INetworkResourceBuilder WithParent(IResourceBuilder resource)
    {
        inner.WithParent(resource);
        return this;
    }

    public INetworkResourceBuilder DependsOn(string resourceId)
    {
        inner.DependsOn(resourceId);
        return this;
    }

    public INetworkResourceBuilder DependsOn(IResourceBuilder resource)
    {
        inner.DependsOn(resource);
        return this;
    }

    public INetworkResourceBuilder DependsOn(IEnumerable<string> resourceIds)
    {
        inner.DependsOn(resourceIds);
        return this;
    }

    public INetworkResourceBuilder DependsOn(IEnumerable<IResourceBuilder> resources)
    {
        inner.DependsOn(resources);
        return this;
    }

    public INetworkResourceBuilder WithReference(string resourceId)
    {
        inner.WithReference(resourceId);
        return this;
    }

    public INetworkResourceBuilder WithReference(IResourceBuilder resource)
    {
        inner.WithReference(resource);
        return this;
    }

    public INetworkResourceBuilder WithReferences(IEnumerable<string> resourceIds)
    {
        inner.WithReferences(resourceIds);
        return this;
    }

    public INetworkResourceBuilder Persist(bool overwrite = false)
    {
        inner.Persist(overwrite);
        return this;
    }

    IResourceBuilder IResourceBuilder.WithResourceGroup(string? resourceGroupId) =>
        WithResourceGroup(resourceGroupId);

    IResourceBuilder IResourceBuilder.WithParent(string? parentResourceId) =>
        WithParent(parentResourceId);

    IResourceBuilder IResourceBuilder.WithParent(IResourceBuilder resource) =>
        WithParent(resource);

    IResourceBuilder IResourceBuilder.DependsOn(string resourceId) =>
        DependsOn(resourceId);

    IResourceBuilder IResourceBuilder.DependsOn(IResourceBuilder resource) =>
        DependsOn(resource);

    IResourceBuilder IResourceBuilder.DependsOn(IEnumerable<string> resourceIds) =>
        DependsOn(resourceIds);

    IResourceBuilder IResourceBuilder.DependsOn(IEnumerable<IResourceBuilder> resources) =>
        DependsOn(resources);

    IResourceBuilder IResourceBuilder.WithReference(string resourceId) =>
        WithReference(resourceId);

    IResourceBuilder IResourceBuilder.WithReference(IResourceBuilder resource) =>
        WithReference(resource);

    IResourceBuilder IResourceBuilder.WithReferences(IEnumerable<string> resourceIds) =>
        WithReferences(resourceIds);

    IResourceBuilder IResourceBuilder.Persist(bool overwrite) =>
        Persist(overwrite);
}

internal sealed class ServiceResourceBuilder(
    IResourceBuilder inner,
    DeclaredServiceResource declared) : IServiceResourceBuilder
{
    public ICloudShellBuilder CloudShellBuilder => inner.CloudShellBuilder;

    public string ResourceId => inner.ResourceId;

    public IServiceResourceBuilder Targets(IResourceBuilder resource, int weight = 100)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return Targets(resource.ResourceId, weight);
    }

    public IServiceResourceBuilder Targets(string resourceId, int weight = 100)
    {
        declared.Definition = declared.Definition with
        {
            Targets = declared.Definition.Targets
                .Append(new ServiceTarget(resourceId, weight))
                .ToArray()
        };
        inner.DependsOn(resourceId);
        return this;
    }

    public IServiceResourceBuilder Targets(IEnumerable<IResourceBuilder> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        foreach (var resource in resources)
        {
            ArgumentNullException.ThrowIfNull(resource);
            Targets(resource);
        }

        return this;
    }

    public IServiceResourceBuilder WithPort(
        string name,
        int targetPort,
        int? port = null,
        string protocol = "tcp",
        ResourceExposureScope exposure = ResourceExposureScope.Network)
    {
        declared.Definition = declared.Definition with
        {
            Ports = declared.Definition.Ports
                .Append(new ServicePort(name, targetPort, port, protocol, exposure))
                .ToArray()
        };
        return this;
    }

    public IServiceResourceBuilder ExposePublic(
        string name = "public",
        int targetPort = 80,
        int? port = null,
        string protocol = "tcp") =>
        WithPort(name, targetPort, port, protocol, ResourceExposureScope.Public);

    public IServiceResourceBuilder WithHttpHealthCheck(
        string path,
        string? endpointName = null,
        string name = "health",
        TimeSpan? timeout = null) =>
        WithHttpProbe(ResourceProbeType.Health, path, endpointName, name, timeout);

    public IServiceResourceBuilder WithHttpProbe(
        ResourceProbeType type,
        string path,
        string? endpointName = null,
        string? name = null,
        TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        declared.Definition = declared.Definition with
        {
            HealthChecks = declared.Definition.ResourceHealthChecks
                .Append(new ResourceHealthCheck(
                    path,
                    type,
                    NormalizeNullable(endpointName),
                    NormalizeNullable(name) ?? type.ToString().ToLowerInvariant(),
                    timeout))
                .ToArray()
        };
        return this;
    }

    public IServiceResourceBuilder WithNetwork(INetworkResourceBuilder network)
    {
        ArgumentNullException.ThrowIfNull(network);
        return WithNetwork(network.ResourceId);
    }

    public IServiceResourceBuilder WithNetwork(string networkId)
    {
        declared.Definition = declared.Definition with
        {
            NetworkIds = declared.Definition.NetworkIds
                .Append(networkId)
                .ToArray()
        };
        inner.DependsOn(networkId);
        return this;
    }

    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public IServiceResourceBuilder WithResourceGroup(string? resourceGroupId)
    {
        inner.WithResourceGroup(resourceGroupId);
        return this;
    }

    public IServiceResourceBuilder WithParent(string? parentResourceId)
    {
        inner.WithParent(parentResourceId);
        return this;
    }

    public IServiceResourceBuilder WithParent(IResourceBuilder resource)
    {
        inner.WithParent(resource);
        return this;
    }

    public IServiceResourceBuilder DependsOn(string resourceId)
    {
        inner.DependsOn(resourceId);
        return this;
    }

    public IServiceResourceBuilder DependsOn(IResourceBuilder resource)
    {
        inner.DependsOn(resource);
        return this;
    }

    public IServiceResourceBuilder DependsOn(IEnumerable<string> resourceIds)
    {
        inner.DependsOn(resourceIds);
        return this;
    }

    public IServiceResourceBuilder DependsOn(IEnumerable<IResourceBuilder> resources)
    {
        inner.DependsOn(resources);
        return this;
    }

    public IServiceResourceBuilder WithReference(string resourceId)
    {
        inner.WithReference(resourceId);
        return this;
    }

    public IServiceResourceBuilder WithReference(IResourceBuilder resource)
    {
        inner.WithReference(resource);
        return this;
    }

    public IServiceResourceBuilder WithReferences(IEnumerable<string> resourceIds)
    {
        inner.WithReferences(resourceIds);
        return this;
    }

    public IServiceResourceBuilder Persist(bool overwrite = false)
    {
        inner.Persist(overwrite);
        return this;
    }

    IResourceBuilder IResourceBuilder.WithResourceGroup(string? resourceGroupId) =>
        WithResourceGroup(resourceGroupId);

    IResourceBuilder IResourceBuilder.WithParent(string? parentResourceId) =>
        WithParent(parentResourceId);

    IResourceBuilder IResourceBuilder.WithParent(IResourceBuilder resource) =>
        WithParent(resource);

    IResourceBuilder IResourceBuilder.DependsOn(string resourceId) =>
        DependsOn(resourceId);

    IResourceBuilder IResourceBuilder.DependsOn(IResourceBuilder resource) =>
        DependsOn(resource);

    IResourceBuilder IResourceBuilder.DependsOn(IEnumerable<string> resourceIds) =>
        DependsOn(resourceIds);

    IResourceBuilder IResourceBuilder.DependsOn(IEnumerable<IResourceBuilder> resources) =>
        DependsOn(resources);

    IResourceBuilder IResourceBuilder.WithReference(string resourceId) =>
        WithReference(resourceId);

    IResourceBuilder IResourceBuilder.WithReference(IResourceBuilder resource) =>
        WithReference(resource);

    IResourceBuilder IResourceBuilder.WithReferences(IEnumerable<string> resourceIds) =>
        WithReferences(resourceIds);

    IResourceBuilder IResourceBuilder.Persist(bool overwrite) =>
        Persist(overwrite);
}
