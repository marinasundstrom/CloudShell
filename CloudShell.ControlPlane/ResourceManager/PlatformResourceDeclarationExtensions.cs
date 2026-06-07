using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.ControlPlane.ResourceManager;

public static class PlatformResourceDeclarationExtensions
{
    public static INetworkResourceBuilder AddNetwork(
        this ICloudShellResourceDeclarationBuilder builder,
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

    public static IServiceResourceBuilder AddService(
        this ICloudShellResourceDeclarationBuilder builder,
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

public interface INetworkResourceBuilder : ICloudShellResourceBuilder
{
    INetworkResourceBuilder AsDefault(bool isDefault = true);

    new INetworkResourceBuilder DependsOn(string resourceId);

    new INetworkResourceBuilder DependsOn(ICloudShellResourceBuilder resource);

    new INetworkResourceBuilder DependsOn(IEnumerable<string> resourceIds);

    new INetworkResourceBuilder DependsOn(IEnumerable<ICloudShellResourceBuilder> resources);

    new INetworkResourceBuilder WithResourceGroup(string? resourceGroupId);

    new INetworkResourceBuilder WithParent(string? parentResourceId);

    new INetworkResourceBuilder WithParent(ICloudShellResourceBuilder resource);

    new INetworkResourceBuilder WithReference(string resourceId);

    new INetworkResourceBuilder WithReference(ICloudShellResourceBuilder resource);

    new INetworkResourceBuilder WithReferences(IEnumerable<string> resourceIds);

    new INetworkResourceBuilder Persist(bool overwrite = false);
}

public interface IServiceResourceBuilder : ICloudShellResourceBuilder
{
    IServiceResourceBuilder Targets(ICloudShellResourceBuilder resource, int weight = 100);

    IServiceResourceBuilder Targets(string resourceId, int weight = 100);

    IServiceResourceBuilder Targets(IEnumerable<ICloudShellResourceBuilder> resources);

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

    IServiceResourceBuilder WithNetwork(INetworkResourceBuilder network);

    IServiceResourceBuilder WithNetwork(string networkId);

    new IServiceResourceBuilder DependsOn(string resourceId);

    new IServiceResourceBuilder DependsOn(ICloudShellResourceBuilder resource);

    new IServiceResourceBuilder DependsOn(IEnumerable<string> resourceIds);

    new IServiceResourceBuilder DependsOn(IEnumerable<ICloudShellResourceBuilder> resources);

    new IServiceResourceBuilder WithResourceGroup(string? resourceGroupId);

    new IServiceResourceBuilder WithParent(string? parentResourceId);

    new IServiceResourceBuilder WithParent(ICloudShellResourceBuilder resource);

    new IServiceResourceBuilder WithReference(string resourceId);

    new IServiceResourceBuilder WithReference(ICloudShellResourceBuilder resource);

    new IServiceResourceBuilder WithReferences(IEnumerable<string> resourceIds);

    new IServiceResourceBuilder Persist(bool overwrite = false);
}

internal sealed class NetworkResourceBuilder(
    ICloudShellResourceBuilder inner,
    DeclaredNetworkResource declared) : INetworkResourceBuilder
{
    public ICloudShellBuilder CloudShellBuilder => inner.CloudShellBuilder;

    public string ResourceId => inner.ResourceId;

    public INetworkResourceBuilder AsDefault(bool isDefault = true)
    {
        declared.Definition = declared.Definition with { IsDefault = isDefault };
        return this;
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

    public INetworkResourceBuilder WithParent(ICloudShellResourceBuilder resource)
    {
        inner.WithParent(resource);
        return this;
    }

    public INetworkResourceBuilder DependsOn(string resourceId)
    {
        inner.DependsOn(resourceId);
        return this;
    }

    public INetworkResourceBuilder DependsOn(ICloudShellResourceBuilder resource)
    {
        inner.DependsOn(resource);
        return this;
    }

    public INetworkResourceBuilder DependsOn(IEnumerable<string> resourceIds)
    {
        inner.DependsOn(resourceIds);
        return this;
    }

    public INetworkResourceBuilder DependsOn(IEnumerable<ICloudShellResourceBuilder> resources)
    {
        inner.DependsOn(resources);
        return this;
    }

    public INetworkResourceBuilder WithReference(string resourceId)
    {
        inner.WithReference(resourceId);
        return this;
    }

    public INetworkResourceBuilder WithReference(ICloudShellResourceBuilder resource)
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

    ICloudShellResourceBuilder ICloudShellResourceBuilder.WithResourceGroup(string? resourceGroupId) =>
        WithResourceGroup(resourceGroupId);

    ICloudShellResourceBuilder ICloudShellResourceBuilder.WithParent(string? parentResourceId) =>
        WithParent(parentResourceId);

    ICloudShellResourceBuilder ICloudShellResourceBuilder.WithParent(ICloudShellResourceBuilder resource) =>
        WithParent(resource);

    ICloudShellResourceBuilder ICloudShellResourceBuilder.DependsOn(string resourceId) =>
        DependsOn(resourceId);

    ICloudShellResourceBuilder ICloudShellResourceBuilder.DependsOn(ICloudShellResourceBuilder resource) =>
        DependsOn(resource);

    ICloudShellResourceBuilder ICloudShellResourceBuilder.DependsOn(IEnumerable<string> resourceIds) =>
        DependsOn(resourceIds);

    ICloudShellResourceBuilder ICloudShellResourceBuilder.DependsOn(IEnumerable<ICloudShellResourceBuilder> resources) =>
        DependsOn(resources);

    ICloudShellResourceBuilder ICloudShellResourceBuilder.WithReference(string resourceId) =>
        WithReference(resourceId);

    ICloudShellResourceBuilder ICloudShellResourceBuilder.WithReference(ICloudShellResourceBuilder resource) =>
        WithReference(resource);

    ICloudShellResourceBuilder ICloudShellResourceBuilder.WithReferences(IEnumerable<string> resourceIds) =>
        WithReferences(resourceIds);

    ICloudShellResourceBuilder ICloudShellResourceBuilder.Persist(bool overwrite) =>
        Persist(overwrite);
}

internal sealed class ServiceResourceBuilder(
    ICloudShellResourceBuilder inner,
    DeclaredServiceResource declared) : IServiceResourceBuilder
{
    public ICloudShellBuilder CloudShellBuilder => inner.CloudShellBuilder;

    public string ResourceId => inner.ResourceId;

    public IServiceResourceBuilder Targets(ICloudShellResourceBuilder resource, int weight = 100)
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

    public IServiceResourceBuilder Targets(IEnumerable<ICloudShellResourceBuilder> resources)
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

    public IServiceResourceBuilder WithParent(ICloudShellResourceBuilder resource)
    {
        inner.WithParent(resource);
        return this;
    }

    public IServiceResourceBuilder DependsOn(string resourceId)
    {
        inner.DependsOn(resourceId);
        return this;
    }

    public IServiceResourceBuilder DependsOn(ICloudShellResourceBuilder resource)
    {
        inner.DependsOn(resource);
        return this;
    }

    public IServiceResourceBuilder DependsOn(IEnumerable<string> resourceIds)
    {
        inner.DependsOn(resourceIds);
        return this;
    }

    public IServiceResourceBuilder DependsOn(IEnumerable<ICloudShellResourceBuilder> resources)
    {
        inner.DependsOn(resources);
        return this;
    }

    public IServiceResourceBuilder WithReference(string resourceId)
    {
        inner.WithReference(resourceId);
        return this;
    }

    public IServiceResourceBuilder WithReference(ICloudShellResourceBuilder resource)
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

    ICloudShellResourceBuilder ICloudShellResourceBuilder.WithResourceGroup(string? resourceGroupId) =>
        WithResourceGroup(resourceGroupId);

    ICloudShellResourceBuilder ICloudShellResourceBuilder.WithParent(string? parentResourceId) =>
        WithParent(parentResourceId);

    ICloudShellResourceBuilder ICloudShellResourceBuilder.WithParent(ICloudShellResourceBuilder resource) =>
        WithParent(resource);

    ICloudShellResourceBuilder ICloudShellResourceBuilder.DependsOn(string resourceId) =>
        DependsOn(resourceId);

    ICloudShellResourceBuilder ICloudShellResourceBuilder.DependsOn(ICloudShellResourceBuilder resource) =>
        DependsOn(resource);

    ICloudShellResourceBuilder ICloudShellResourceBuilder.DependsOn(IEnumerable<string> resourceIds) =>
        DependsOn(resourceIds);

    ICloudShellResourceBuilder ICloudShellResourceBuilder.DependsOn(IEnumerable<ICloudShellResourceBuilder> resources) =>
        DependsOn(resources);

    ICloudShellResourceBuilder ICloudShellResourceBuilder.WithReference(string resourceId) =>
        WithReference(resourceId);

    ICloudShellResourceBuilder ICloudShellResourceBuilder.WithReference(ICloudShellResourceBuilder resource) =>
        WithReference(resource);

    ICloudShellResourceBuilder ICloudShellResourceBuilder.WithReferences(IEnumerable<string> resourceIds) =>
        WithReferences(resourceIds);

    ICloudShellResourceBuilder ICloudShellResourceBuilder.Persist(bool overwrite) =>
        Persist(overwrite);
}
