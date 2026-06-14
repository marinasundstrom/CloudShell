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

    public static ILoadBalancerResourceBuilder AddLoadBalancer(
        this IResourceDeclarationBuilder builder,
        string id,
        string? name = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var normalizedId = NormalizeLoadBalancerId(id);
        var definition = new LoadBalancerResourceDefinition(
            normalizedId,
            string.IsNullOrWhiteSpace(name) ? CreateDisplayName(normalizedId) : name.Trim(),
            "traefik");
        var declared = new DeclaredLoadBalancerResource(definition);
        builder.Services
            .GetOrAddPlatformResourceOptions()
            .DeclaredLoadBalancers
            .Add(declared);

        var resource = builder.Declare(
            PlatformResourceProvider.ProviderId,
            definition.Id,
            onChanged: declaration =>
            {
                declared.Persist = declaration.Persistence == ResourceDeclarationPersistence.Persisted;
                declared.OverwritePersistedState = declaration.OverwritePersistedState;
            });

        return new LoadBalancerResourceBuilder(resource, declared);
    }

    public static IDnsZoneResourceBuilder AddDnsZone(
        this IResourceDeclarationBuilder builder,
        string id,
        string? name = null,
        string? zoneName = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var normalizedId = NormalizeDnsZoneId(id);
        var displayName = string.IsNullOrWhiteSpace(name) ? CreateDisplayName(normalizedId) : name.Trim();
        var definition = new DnsZoneResourceDefinition(
            normalizedId,
            displayName,
            string.IsNullOrWhiteSpace(zoneName) ? CreateZoneName(normalizedId) : zoneName.Trim().ToLowerInvariant());
        var declared = new DeclaredDnsZoneResource(definition);
        builder.Services
            .GetOrAddPlatformResourceOptions()
            .DeclaredDnsZones
            .Add(declared);

        var resource = builder.Declare(
            PlatformResourceProvider.ProviderId,
            definition.Id,
            resourceClass: ResourceClass.Network,
            attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ResourceAttributeNames.DnsZoneName] = definition.ZoneName,
                [ResourceAttributeNames.DnsProvider] = "logical"
            },
            onChanged: declaration =>
            {
                declared.Persist = declaration.Persistence == ResourceDeclarationPersistence.Persisted;
                declared.OverwritePersistedState = declaration.OverwritePersistedState;
            });

        return new DnsZoneResourceBuilder(resource, declared);
    }

    public static IStorageResourceBuilder AddLocalStorage(
        this IResourceDeclarationBuilder builder,
        string id,
        string? name = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var normalizedId = NormalizeStorageId(id);
        var definition = new StorageResourceDefinition(
            normalizedId,
            string.IsNullOrWhiteSpace(name) ? CreateDisplayName(normalizedId) : name.Trim(),
            StorageProviderNames.LocalStorage,
            StorageMedia.FileSystem);
        var declared = new DeclaredStorageResource(definition);
        builder.Services
            .GetOrAddPlatformResourceOptions()
            .DeclaredStorages
            .Add(declared);

        var resource = builder.Declare(
            PlatformResourceProvider.ProviderId,
            definition.Id,
            resourceClass: ResourceClass.Storage,
            attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ResourceAttributeNames.StorageProvider] = StorageProviderNames.LocalStorage,
                [ResourceAttributeNames.StorageMedium] = StorageMedia.FileSystem
            },
            onChanged: declaration =>
            {
                declared.Persist = declaration.Persistence == ResourceDeclarationPersistence.Persisted;
                declared.OverwritePersistedState = declaration.OverwritePersistedState;
            });

        return new StorageResourceBuilder(resource, declared);
    }

    public static IVolumeResourceBuilder AddVolume(
        this IResourceDeclarationBuilder builder,
        string id,
        string? name = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var normalizedId = NormalizeVolumeId(id);
        var definition = new VolumeResourceDefinition(
            normalizedId,
            string.IsNullOrWhiteSpace(name) ? CreateDisplayName(normalizedId) : name.Trim());
        var declared = new DeclaredVolumeResource(definition);
        builder.Services
            .GetOrAddPlatformResourceOptions()
            .DeclaredVolumes
            .Add(declared);

        var resource = builder.Declare(
            PlatformResourceProvider.ProviderId,
            definition.Id,
            resourceClass: ResourceClass.Storage,
            attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ResourceAttributeNames.VolumeProvider] = StorageProviderNames.LocalStorage,
                [ResourceAttributeNames.VolumeStorageMedium] = StorageMedia.FileSystem,
                [ResourceAttributeNames.VolumePersistent] = "true"
            },
            onChanged: declaration =>
            {
                declared.Persist = declaration.Persistence == ResourceDeclarationPersistence.Persisted;
                declared.OverwritePersistedState = declaration.OverwritePersistedState;
            });

        return new VolumeResourceBuilder(resource, declared);
    }

    public static IResourceBuilder AddMacOSHostNetworking(
        this IResourceDeclarationBuilder builder,
        string? resourceGroupId = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Declare(
            MacOSHostNetworkProvider.ProviderId,
            MacOSHostNetworkProvider.ResourceId,
            resourceGroupId: resourceGroupId,
            resourceClass: ResourceClass.Infrastructure,
            attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ResourceAttributeNames.InfrastructureKind] = "hostNetworking",
                ["host.os"] = "macos",
                ["networking.mode"] = "localProxy"
            });
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

    private static string NormalizeLoadBalancerId(string id)
    {
        var normalized = id.Trim();
        return normalized.Contains(':', StringComparison.Ordinal)
            ? normalized
            : $"load-balancer:{normalized}";
    }

    private static string NormalizeVolumeId(string id)
    {
        var normalized = id.Trim();
        return normalized.Contains(':', StringComparison.Ordinal)
            ? normalized
            : $"volume:{normalized}";
    }

    private static string NormalizeStorageId(string id)
    {
        var normalized = id.Trim();
        return normalized.Contains(':', StringComparison.Ordinal)
            ? normalized
            : $"storage:{normalized}";
    }

    private static string NormalizeDnsZoneId(string id)
    {
        var normalized = id.Trim();
        return normalized.Contains(':', StringComparison.Ordinal)
            ? normalized
            : $"dns:{normalized}";
    }

    private static string CreateDisplayName(string resourceId)
    {
        var name = resourceId.Contains(':', StringComparison.Ordinal)
            ? resourceId[(resourceId.IndexOf(':', StringComparison.Ordinal) + 1)..]
            : resourceId;
        return string.Join(
            " ",
            name.Split(['-', '_', '.', ':'], StringSplitOptions.RemoveEmptyEntries)
                .Select(segment => string.Concat(segment[..1].ToUpperInvariant(), segment[1..])));
    }

    private static string CreateZoneName(string resourceId) =>
        resourceId.Contains(':', StringComparison.Ordinal)
            ? resourceId[(resourceId.IndexOf(':', StringComparison.Ordinal) + 1)..].Trim().ToLowerInvariant()
            : resourceId.Trim().ToLowerInvariant();
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

public interface ILoadBalancerResourceBuilder : IResourceBuilder
{
    ILoadBalancerResourceBuilder UseProvider(string provider);

    ILoadBalancerResourceBuilder UseDefaultHost();

    ILoadBalancerResourceBuilder UseHost(string hostResourceId);

    ILoadBalancerResourceBuilder UseHost(IResourceBuilder host);

    ILoadBalancerResourceBuilder ExposeHttp(
        int port = 80,
        string name = "http",
        ResourceExposureScope exposure = ResourceExposureScope.Public);

    ILoadBalancerResourceBuilder ExposeHttps(
        int port = 443,
        string name = "https",
        ResourceExposureScope exposure = ResourceExposureScope.Public);

    ILoadBalancerResourceBuilder ExposeTcp(
        int port,
        string? name = null,
        ResourceExposureScope exposure = ResourceExposureScope.Public);

    ILoadBalancerResourceBuilder MapHost(
        string host,
        IResourceBuilder target,
        string endpoint = "http",
        string? id = null,
        string entrypoint = "http");

    ILoadBalancerResourceBuilder MapHost(
        string host,
        IResourceBuilder target,
        int port,
        string? id = null,
        string entrypoint = "http");

    ILoadBalancerResourceBuilder MapPath(
        string host,
        string pathPrefix,
        IResourceBuilder target,
        string endpoint = "http",
        string? id = null,
        string entrypoint = "http");

    ILoadBalancerResourceBuilder MapPath(
        string host,
        string pathPrefix,
        IResourceBuilder target,
        int port,
        string? id = null,
        string entrypoint = "http");

    ILoadBalancerResourceBuilder MapTcp(
        int port,
        IResourceBuilder target,
        string endpoint = "tcp",
        string? id = null,
        string? entrypoint = null);

    ILoadBalancerResourceBuilder MapTcp(
        int port,
        IResourceBuilder target,
        int targetPort,
        string? id = null,
        string? entrypoint = null);

    new ILoadBalancerResourceBuilder DependsOn(string resourceId);

    new ILoadBalancerResourceBuilder DependsOn(IResourceBuilder resource);

    new ILoadBalancerResourceBuilder DependsOn(IEnumerable<string> resourceIds);

    new ILoadBalancerResourceBuilder DependsOn(IEnumerable<IResourceBuilder> resources);

    new ILoadBalancerResourceBuilder WithResourceGroup(string? resourceGroupId);

    new ILoadBalancerResourceBuilder WithParent(string? parentResourceId);

    new ILoadBalancerResourceBuilder WithParent(IResourceBuilder resource);

    new ILoadBalancerResourceBuilder WithReference(string resourceId);

    new ILoadBalancerResourceBuilder WithReference(IResourceBuilder resource);

    new ILoadBalancerResourceBuilder WithReferences(IEnumerable<string> resourceIds);

    new ILoadBalancerResourceBuilder Persist(bool overwrite = false);
}

public interface IVolumeResourceBuilder : IResourceBuilder
{
    IVolumeResourceBuilder UseProvider(string provider);

    IVolumeResourceBuilder UseLocation(string location);

    IVolumeResourceBuilder UseHostPath(string path);

    IVolumeResourceBuilder UseStorage(string storageResourceId, string subPath);

    IVolumeResourceBuilder UseStorage(IStorageResourceBuilder storage, string subPath);

    IVolumeResourceBuilder WithAccessMode(VolumeAccessMode accessMode);

    IVolumeResourceBuilder AsPersistent(bool persistent = true);

    new IVolumeResourceBuilder DependsOn(string resourceId);

    new IVolumeResourceBuilder DependsOn(IResourceBuilder resource);

    new IVolumeResourceBuilder DependsOn(IEnumerable<string> resourceIds);

    new IVolumeResourceBuilder DependsOn(IEnumerable<IResourceBuilder> resources);

    new IVolumeResourceBuilder WithResourceGroup(string? resourceGroupId);

    new IVolumeResourceBuilder WithParent(string? parentResourceId);

    new IVolumeResourceBuilder WithParent(IResourceBuilder resource);

    new IVolumeResourceBuilder WithReference(string resourceId);

    new IVolumeResourceBuilder WithReference(IResourceBuilder resource);

    new IVolumeResourceBuilder WithReferences(IEnumerable<string> resourceIds);

    new IVolumeResourceBuilder Persist(bool overwrite = false);
}

public interface IStorageResourceBuilder : IResourceBuilder
{
    IStorageResourceBuilder UseLocation(string location);

    new IStorageResourceBuilder DependsOn(string resourceId);

    new IStorageResourceBuilder DependsOn(IResourceBuilder resource);

    new IStorageResourceBuilder DependsOn(IEnumerable<string> resourceIds);

    new IStorageResourceBuilder DependsOn(IEnumerable<IResourceBuilder> resources);

    new IStorageResourceBuilder WithResourceGroup(string? resourceGroupId);

    new IStorageResourceBuilder WithParent(string? parentResourceId);

    new IStorageResourceBuilder WithParent(IResourceBuilder resource);

    new IStorageResourceBuilder WithReference(string resourceId);

    new IStorageResourceBuilder WithReference(IResourceBuilder resource);

    new IStorageResourceBuilder WithReferences(IEnumerable<string> resourceIds);

    new IStorageResourceBuilder Persist(bool overwrite = false);
}

public interface IDnsZoneResourceBuilder : IResourceBuilder
{
    IDnsZoneResourceBuilder UseProvider(string provider);

    IDnsZoneResourceBuilder MapHost(
        string hostName,
        IResourceBuilder target,
        string? endpointName = null,
        string? id = null,
        string? name = null,
        ResourceExposureScope exposure = ResourceExposureScope.Public,
        string? providerResourceId = null);

    IDnsZoneResourceBuilder MapHost(
        string hostName,
        ResourceEndpointReference target,
        string? id = null,
        string? name = null,
        ResourceExposureScope exposure = ResourceExposureScope.Public,
        string? providerResourceId = null);

    new IDnsZoneResourceBuilder DependsOn(string resourceId);

    new IDnsZoneResourceBuilder DependsOn(IResourceBuilder resource);

    new IDnsZoneResourceBuilder DependsOn(IEnumerable<string> resourceIds);

    new IDnsZoneResourceBuilder DependsOn(IEnumerable<IResourceBuilder> resources);

    new IDnsZoneResourceBuilder WithResourceGroup(string? resourceGroupId);

    new IDnsZoneResourceBuilder WithParent(string? parentResourceId);

    new IDnsZoneResourceBuilder WithParent(IResourceBuilder resource);

    new IDnsZoneResourceBuilder WithReference(string resourceId);

    new IDnsZoneResourceBuilder WithReference(IResourceBuilder resource);

    new IDnsZoneResourceBuilder WithReferences(IEnumerable<string> resourceIds);

    new IDnsZoneResourceBuilder Persist(bool overwrite = false);
}

internal sealed class DnsZoneResourceBuilder(
    IResourceBuilder inner,
    DeclaredDnsZoneResource declared) : IDnsZoneResourceBuilder
{
    public ICloudShellBuilder CloudShellBuilder => inner.CloudShellBuilder;

    public string ResourceId => inner.ResourceId;

    public ResourceIdentityReference Identity => inner.Identity;

    public IDnsZoneResourceBuilder UseProvider(string provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        var normalized = provider.Trim();
        declared.Definition = declared.Definition with { Provider = normalized };
        inner.WithResourceAttribute(ResourceAttributeNames.DnsProvider, normalized);
        return this;
    }

    public IDnsZoneResourceBuilder MapHost(
        string hostName,
        IResourceBuilder target,
        string? endpointName = null,
        string? id = null,
        string? name = null,
        ResourceExposureScope exposure = ResourceExposureScope.Public,
        string? providerResourceId = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        return MapHost(
            hostName,
            new ResourceEndpointReference(target.ResourceId, string.IsNullOrWhiteSpace(endpointName) ? "http" : endpointName.Trim()),
            id,
            name,
            exposure,
            providerResourceId);
    }

    public IDnsZoneResourceBuilder MapHost(
        string hostName,
        ResourceEndpointReference target,
        string? id = null,
        string? name = null,
        ResourceExposureScope exposure = ResourceExposureScope.Public,
        string? providerResourceId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostName);
        ArgumentNullException.ThrowIfNull(target);

        var normalizedHost = hostName.Trim().ToLowerInvariant();
        var mappingId = string.IsNullOrWhiteSpace(id)
            ? $"{ResourceId}:name:{CreateStableIdentifier(normalizedHost)}"
            : id.Trim();
        declared.Definition = declared.Definition with
        {
            Mappings = declared.Definition.DnsNameMappings
                .Where(mapping => !string.Equals(mapping.Id, mappingId, StringComparison.OrdinalIgnoreCase))
                .Append(new DnsNameMappingDefinition(
                    mappingId,
                    string.IsNullOrWhiteSpace(name) ? normalizedHost : name.Trim(),
                    normalizedHost,
                    target.ResourceId.Trim(),
                    string.IsNullOrWhiteSpace(target.EndpointName) ? null : target.EndpointName.Trim(),
                    exposure,
                    string.IsNullOrWhiteSpace(providerResourceId) ? null : providerResourceId.Trim()))
                .ToArray()
        };

        inner.DependsOn(target.ResourceId);
        if (!string.IsNullOrWhiteSpace(providerResourceId))
        {
            inner.DependsOn(providerResourceId.Trim());
        }

        return this;
    }

    private static string CreateStableIdentifier(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '-');
        }

        var identifier = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(identifier) ? "name" : identifier;
    }

    public IDnsZoneResourceBuilder WithResourceGroup(string? resourceGroupId)
    {
        inner.WithResourceGroup(resourceGroupId);
        return this;
    }

    public IDnsZoneResourceBuilder WithParent(string? parentResourceId)
    {
        inner.WithParent(parentResourceId);
        return this;
    }

    public IDnsZoneResourceBuilder WithParent(IResourceBuilder resource)
    {
        inner.WithParent(resource);
        return this;
    }

    public IDnsZoneResourceBuilder DependsOn(string resourceId)
    {
        inner.DependsOn(resourceId);
        return this;
    }

    public IDnsZoneResourceBuilder DependsOn(IResourceBuilder resource)
    {
        inner.DependsOn(resource);
        return this;
    }

    public IDnsZoneResourceBuilder DependsOn(IEnumerable<string> resourceIds)
    {
        inner.DependsOn(resourceIds);
        return this;
    }

    public IDnsZoneResourceBuilder DependsOn(IEnumerable<IResourceBuilder> resources)
    {
        inner.DependsOn(resources);
        return this;
    }

    public IDnsZoneResourceBuilder WithReference(string resourceId)
    {
        inner.WithReference(resourceId);
        return this;
    }

    public IDnsZoneResourceBuilder WithReference(IResourceBuilder resource)
    {
        inner.WithReference(resource);
        return this;
    }

    public IDnsZoneResourceBuilder WithReferences(IEnumerable<string> resourceIds)
    {
        inner.WithReferences(resourceIds);
        return this;
    }

    public IDnsZoneResourceBuilder Persist(bool overwrite = false)
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

internal sealed class NetworkResourceBuilder(
    IResourceBuilder inner,
    DeclaredNetworkResource declared) : INetworkResourceBuilder
{
    public ICloudShellBuilder CloudShellBuilder => inner.CloudShellBuilder;

    public string ResourceId => inner.ResourceId;

    public ResourceIdentityReference Identity => inner.Identity;

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

    public ResourceIdentityReference Identity => inner.Identity;

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

internal sealed class VolumeResourceBuilder(
    IResourceBuilder inner,
    DeclaredVolumeResource declared) : IVolumeResourceBuilder
{
    public ICloudShellBuilder CloudShellBuilder => inner.CloudShellBuilder;

    public string ResourceId => inner.ResourceId;

    public ResourceIdentityReference Identity => inner.Identity;

    public IVolumeResourceBuilder UseProvider(string provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        declared.Definition = declared.Definition with
        {
            Provider = provider.Trim()
        };
        inner.WithResourceAttribute(ResourceAttributeNames.VolumeProvider, provider.Trim());
        return this;
    }

    public IVolumeResourceBuilder UseLocation(string location)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        declared.Definition = declared.Definition with
        {
            Location = location.Trim()
        };
        inner.WithResourceAttribute(ResourceAttributeNames.VolumeLocation, location.Trim());
        return this;
    }

    public IVolumeResourceBuilder UseHostPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return UseProvider(StorageProviderNames.LocalStorage)
            .UseLocation(path);
    }

    public IVolumeResourceBuilder UseStorage(string storageResourceId, string subPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageResourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(subPath);
        declared.Definition = declared.Definition with
        {
            Provider = StorageProviderNames.LocalStorage,
            Location = null,
            StorageResourceId = storageResourceId.Trim(),
            SubPath = subPath.Trim()
        };
        inner.WithResourceAttribute(ResourceAttributeNames.VolumeProvider, StorageProviderNames.LocalStorage);
        inner.WithResourceAttribute(ResourceAttributeNames.VolumeStorageMedium, StorageMedia.FileSystem);
        inner.WithResourceAttribute(ResourceAttributeNames.VolumeStorageResourceId, storageResourceId.Trim());
        inner.WithResourceAttribute(ResourceAttributeNames.VolumeSubPath, subPath.Trim());
        inner.DependsOn(storageResourceId.Trim());
        inner.WithParent(storageResourceId.Trim());
        return this;
    }

    public IVolumeResourceBuilder UseStorage(IStorageResourceBuilder storage, string subPath)
    {
        ArgumentNullException.ThrowIfNull(storage);
        return UseStorage(storage.ResourceId, subPath);
    }

    public IVolumeResourceBuilder WithAccessMode(VolumeAccessMode accessMode)
    {
        declared.Definition = declared.Definition with
        {
            AccessMode = accessMode
        };
        inner.WithResourceAttribute(ResourceAttributeNames.VolumeAccessMode, accessMode.ToString());
        return this;
    }

    public IVolumeResourceBuilder AsPersistent(bool persistent = true)
    {
        declared.Definition = declared.Definition with
        {
            Persistent = persistent
        };
        inner.WithResourceAttribute(
            ResourceAttributeNames.VolumePersistent,
            persistent.ToString().ToLowerInvariant());
        return this;
    }

    public IVolumeResourceBuilder WithResourceGroup(string? resourceGroupId)
    {
        inner.WithResourceGroup(resourceGroupId);
        return this;
    }

    public IVolumeResourceBuilder WithParent(string? parentResourceId)
    {
        inner.WithParent(parentResourceId);
        return this;
    }

    public IVolumeResourceBuilder WithParent(IResourceBuilder resource)
    {
        inner.WithParent(resource);
        return this;
    }

    public IVolumeResourceBuilder DependsOn(string resourceId)
    {
        inner.DependsOn(resourceId);
        return this;
    }

    public IVolumeResourceBuilder DependsOn(IResourceBuilder resource)
    {
        inner.DependsOn(resource);
        return this;
    }

    public IVolumeResourceBuilder DependsOn(IEnumerable<string> resourceIds)
    {
        inner.DependsOn(resourceIds);
        return this;
    }

    public IVolumeResourceBuilder DependsOn(IEnumerable<IResourceBuilder> resources)
    {
        inner.DependsOn(resources);
        return this;
    }

    public IVolumeResourceBuilder WithReference(string resourceId)
    {
        inner.WithReference(resourceId);
        return this;
    }

    public IVolumeResourceBuilder WithReference(IResourceBuilder resource)
    {
        inner.WithReference(resource);
        return this;
    }

    public IVolumeResourceBuilder WithReferences(IEnumerable<string> resourceIds)
    {
        inner.WithReferences(resourceIds);
        return this;
    }

    public IVolumeResourceBuilder Persist(bool overwrite = false)
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

internal sealed class StorageResourceBuilder(
    IResourceBuilder inner,
    DeclaredStorageResource declared) : IStorageResourceBuilder
{
    public ICloudShellBuilder CloudShellBuilder => inner.CloudShellBuilder;

    public string ResourceId => inner.ResourceId;

    public ResourceIdentityReference Identity => inner.Identity;

    public IStorageResourceBuilder UseLocation(string location)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        declared.Definition = declared.Definition with
        {
            Location = location.Trim()
        };
        inner.WithResourceAttribute(ResourceAttributeNames.StorageLocation, location.Trim());
        return this;
    }

    public IStorageResourceBuilder WithResourceGroup(string? resourceGroupId)
    {
        inner.WithResourceGroup(resourceGroupId);
        return this;
    }

    public IStorageResourceBuilder WithParent(string? parentResourceId)
    {
        inner.WithParent(parentResourceId);
        return this;
    }

    public IStorageResourceBuilder WithParent(IResourceBuilder resource)
    {
        inner.WithParent(resource);
        return this;
    }

    public IStorageResourceBuilder DependsOn(string resourceId)
    {
        inner.DependsOn(resourceId);
        return this;
    }

    public IStorageResourceBuilder DependsOn(IResourceBuilder resource)
    {
        inner.DependsOn(resource);
        return this;
    }

    public IStorageResourceBuilder DependsOn(IEnumerable<string> resourceIds)
    {
        inner.DependsOn(resourceIds);
        return this;
    }

    public IStorageResourceBuilder DependsOn(IEnumerable<IResourceBuilder> resources)
    {
        inner.DependsOn(resources);
        return this;
    }

    public IStorageResourceBuilder WithReference(string resourceId)
    {
        inner.WithReference(resourceId);
        return this;
    }

    public IStorageResourceBuilder WithReference(IResourceBuilder resource)
    {
        inner.WithReference(resource);
        return this;
    }

    public IStorageResourceBuilder WithReferences(IEnumerable<string> resourceIds)
    {
        inner.WithReferences(resourceIds);
        return this;
    }

    public IStorageResourceBuilder Persist(bool overwrite = false)
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

internal sealed class LoadBalancerResourceBuilder(
    IResourceBuilder inner,
    DeclaredLoadBalancerResource declared) : ILoadBalancerResourceBuilder
{
    public ICloudShellBuilder CloudShellBuilder => inner.CloudShellBuilder;

    public string ResourceId => inner.ResourceId;

    public ResourceIdentityReference Identity => inner.Identity;

    public ILoadBalancerResourceBuilder UseProvider(string provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        declared.Definition = declared.Definition with
        {
            Provider = provider.Trim().ToLowerInvariant()
        };
        return this;
    }

    public ILoadBalancerResourceBuilder UseDefaultHost()
    {
        declared.Definition = declared.Definition with { HostResourceId = null };
        return this;
    }

    public ILoadBalancerResourceBuilder UseHost(string hostResourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostResourceId);
        var normalizedHostResourceId = hostResourceId.Trim();
        declared.Definition = declared.Definition with
        {
            HostResourceId = normalizedHostResourceId
        };
        inner.DependsOn(normalizedHostResourceId);
        return this;
    }

    public ILoadBalancerResourceBuilder UseHost(IResourceBuilder host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return UseHost(host.ResourceId);
    }

    public ILoadBalancerResourceBuilder ExposeHttp(
        int port = 80,
        string name = "http",
        ResourceExposureScope exposure = ResourceExposureScope.Public) =>
        AddEntrypoint(name, ResourceEndpointProtocol.Http, port, exposure);

    public ILoadBalancerResourceBuilder ExposeHttps(
        int port = 443,
        string name = "https",
        ResourceExposureScope exposure = ResourceExposureScope.Public) =>
        AddEntrypoint(name, ResourceEndpointProtocol.Https, port, exposure);

    public ILoadBalancerResourceBuilder ExposeTcp(
        int port,
        string? name = null,
        ResourceExposureScope exposure = ResourceExposureScope.Public) =>
        AddEntrypoint(name ?? $"tcp-{port}", ResourceEndpointProtocol.Tcp, port, exposure);

    public ILoadBalancerResourceBuilder MapHost(
        string host,
        IResourceBuilder target,
        string endpoint = "http",
        string? id = null,
        string entrypoint = "http") =>
        AddRoute(
            LoadBalancerRouteKind.Http,
            id,
            $"{host} to {target.ResourceId}",
            entrypoint,
            new LoadBalancerRouteMatch(Host: host),
            new LoadBalancerRouteTarget(target.ResourceId, EndpointName: endpoint),
            target);

    public ILoadBalancerResourceBuilder MapHost(
        string host,
        IResourceBuilder target,
        int port,
        string? id = null,
        string entrypoint = "http") =>
        AddRoute(
            LoadBalancerRouteKind.Http,
            id,
            $"{host} to {target.ResourceId}:{port}",
            entrypoint,
            new LoadBalancerRouteMatch(Host: host),
            new LoadBalancerRouteTarget(target.ResourceId, Port: port),
            target);

    public ILoadBalancerResourceBuilder MapPath(
        string host,
        string pathPrefix,
        IResourceBuilder target,
        string endpoint = "http",
        string? id = null,
        string entrypoint = "http") =>
        AddRoute(
            LoadBalancerRouteKind.Http,
            id,
            $"{host}{pathPrefix} to {target.ResourceId}",
            entrypoint,
            new LoadBalancerRouteMatch(Host: host, PathPrefix: pathPrefix),
            new LoadBalancerRouteTarget(target.ResourceId, EndpointName: endpoint),
            target);

    public ILoadBalancerResourceBuilder MapPath(
        string host,
        string pathPrefix,
        IResourceBuilder target,
        int port,
        string? id = null,
        string entrypoint = "http") =>
        AddRoute(
            LoadBalancerRouteKind.Http,
            id,
            $"{host}{pathPrefix} to {target.ResourceId}:{port}",
            entrypoint,
            new LoadBalancerRouteMatch(Host: host, PathPrefix: pathPrefix),
            new LoadBalancerRouteTarget(target.ResourceId, Port: port),
            target);

    public ILoadBalancerResourceBuilder MapTcp(
        int port,
        IResourceBuilder target,
        string endpoint = "tcp",
        string? id = null,
        string? entrypoint = null) =>
        AddRoute(
            LoadBalancerRouteKind.Tcp,
            id,
            $"tcp {port} to {target.ResourceId}",
            entrypoint ?? $"tcp-{port}",
            new LoadBalancerRouteMatch(Port: port),
            new LoadBalancerRouteTarget(target.ResourceId, EndpointName: endpoint),
            target);

    public ILoadBalancerResourceBuilder MapTcp(
        int port,
        IResourceBuilder target,
        int targetPort,
        string? id = null,
        string? entrypoint = null) =>
        AddRoute(
            LoadBalancerRouteKind.Tcp,
            id,
            $"tcp {port} to {target.ResourceId}:{targetPort}",
            entrypoint ?? $"tcp-{port}",
            new LoadBalancerRouteMatch(Port: port),
            new LoadBalancerRouteTarget(target.ResourceId, Port: targetPort),
            target);

    private ILoadBalancerResourceBuilder AddEntrypoint(
        string name,
        ResourceEndpointProtocol protocol,
        int port,
        ResourceExposureScope exposure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var normalizedName = name.Trim();
        declared.Definition = declared.Definition with
        {
            Entrypoints = declared.Definition.LoadBalancerEntrypoints
                .Where(entrypoint => !string.Equals(entrypoint.Name, normalizedName, StringComparison.OrdinalIgnoreCase))
                .Append(new LoadBalancerEntrypoint(
                    normalizedName,
                    protocol,
                    port,
                    exposure))
                .ToArray()
        };
        return this;
    }

    private ILoadBalancerResourceBuilder AddRoute(
        LoadBalancerRouteKind kind,
        string? id,
        string name,
        string entrypoint,
        LoadBalancerRouteMatch match,
        LoadBalancerRouteTarget target,
        IResourceBuilder targetResource)
    {
        ArgumentNullException.ThrowIfNull(targetResource);
        ArgumentException.ThrowIfNullOrWhiteSpace(entrypoint);

        var routeId = string.IsNullOrWhiteSpace(id)
            ? CreateRouteId(kind, match, target)
            : id.Trim();
        declared.Definition = declared.Definition with
        {
            Routes = declared.Definition.LoadBalancerRoutes
                .Where(route => !string.Equals(route.Id, routeId, StringComparison.OrdinalIgnoreCase))
                .Append(new LoadBalancerRoute(
                    routeId,
                    name.Trim(),
                    kind,
                    entrypoint.Trim(),
                    match,
                    target))
                .ToArray()
        };
        inner.DependsOn(targetResource);
        return this;
    }

    private string CreateRouteId(
        LoadBalancerRouteKind kind,
        LoadBalancerRouteMatch match,
        LoadBalancerRouteTarget target)
    {
        var source = kind == LoadBalancerRouteKind.Tcp
            ? $"tcp-{match.Port}"
            : string.Join(
                "-",
                new[] { match.Host, match.PathPrefix }
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value!
                        .Trim()
                        .Replace("/", "-", StringComparison.Ordinal)
                        .Trim('-')));
        var targetPart = target.EndpointName ?? target.Port?.ToString() ?? "target";
        return $"{ResourceId}:route:{source}:{target.ResourceId}:{targetPart}";
    }

    public ILoadBalancerResourceBuilder WithResourceGroup(string? resourceGroupId)
    {
        inner.WithResourceGroup(resourceGroupId);
        return this;
    }

    public ILoadBalancerResourceBuilder WithParent(string? parentResourceId)
    {
        inner.WithParent(parentResourceId);
        return this;
    }

    public ILoadBalancerResourceBuilder WithParent(IResourceBuilder resource)
    {
        inner.WithParent(resource);
        return this;
    }

    public ILoadBalancerResourceBuilder DependsOn(string resourceId)
    {
        inner.DependsOn(resourceId);
        return this;
    }

    public ILoadBalancerResourceBuilder DependsOn(IResourceBuilder resource)
    {
        inner.DependsOn(resource);
        return this;
    }

    public ILoadBalancerResourceBuilder DependsOn(IEnumerable<string> resourceIds)
    {
        inner.DependsOn(resourceIds);
        return this;
    }

    public ILoadBalancerResourceBuilder DependsOn(IEnumerable<IResourceBuilder> resources)
    {
        inner.DependsOn(resources);
        return this;
    }

    public ILoadBalancerResourceBuilder WithReference(string resourceId)
    {
        inner.WithReference(resourceId);
        return this;
    }

    public ILoadBalancerResourceBuilder WithReference(IResourceBuilder resource)
    {
        inner.WithReference(resource);
        return this;
    }

    public ILoadBalancerResourceBuilder WithReferences(IEnumerable<string> resourceIds)
    {
        inner.WithReferences(resourceIds);
        return this;
    }

    public ILoadBalancerResourceBuilder Persist(bool overwrite = false)
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
