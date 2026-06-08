using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.Providers.Docker;

public static class DockerProviderServiceCollectionExtensions
{
    public static ICloudShellBuilder UseDocker(
        this ICloudShellBuilder builder,
        Action<DockerProviderOptions>? configure = null)
    {
        UseDockerCore(builder, configure);
        return builder;
    }

    public static IControlPlaneBuilder UseDocker(
        this IControlPlaneBuilder builder,
        Action<DockerProviderOptions>? configure = null)
    {
        UseDockerCore(builder, configure);
        return builder;
    }

    public static ICloudShellBuilder AddDockerProvider(
        this ICloudShellBuilder builder,
        Action<DockerProviderOptions>? configure = null,
        CloudShellExtensionActivationPolicy activationPolicy = CloudShellExtensionActivationPolicy.Enabled)
    {
        AddDockerProviderCore(builder, configure);
        return builder.AddExtension(new DockerProviderExtension(), activationPolicy);
    }

    public static IControlPlaneBuilder AddDockerProvider(
        this IControlPlaneBuilder builder,
        Action<DockerProviderOptions>? configure = null,
        CloudShellExtensionActivationPolicy activationPolicy = CloudShellExtensionActivationPolicy.Enabled)
    {
        AddDockerProviderCore(builder, configure);
        return builder.AddExtension(new DockerProviderExtension(), activationPolicy);
    }

    private static void AddDockerProviderCore(
        ICloudShellBuilder builder,
        Action<DockerProviderOptions>? configure)
    {
        UseDockerCore(builder, configure);
    }

    private static void UseDockerCore(
        ICloudShellBuilder builder,
        Action<DockerProviderOptions>? configure)
    {
        var options = builder.Services.GetOrAddDockerProviderOptions();
        configure?.Invoke(options);
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IContainerEngineProvider, DockerContainerEngineProvider>());
    }

    public static IDockerResourceBuilder AddDocker(
        this IResourceDeclarationBuilder builder) =>
        builder.AddDocker(DockerContainerResourceProvider.EngineResourceId, "Local Docker Engine");

    public static IDockerResourceBuilder AddDocker(
        this IResourceDeclarationBuilder builder,
        string id,
        string name)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var definition = new DockerResourceDefinition(id, name);
        var declared = new DeclaredDockerResource(definition);
        builder.Services
            .GetOrAddDockerProviderOptions()
            .DeclaredDockerResources
            .Add(declared);

        var resource = builder.Declare(
            "docker",
            definition.Id,
            onChanged: declaration =>
            {
                declared.Persist = declaration.Persistence == ResourceDeclarationPersistence.Persisted;
                declared.OverwritePersistedState = declaration.OverwritePersistedState;
            });

        return new DockerResourceBuilder(builder, resource);
    }

    public static IResourceBuilder AddDockerEngine(
        this IResourceDeclarationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Declare("docker", DockerContainerResourceProvider.EngineResourceId);
    }

    public static IDockerContainerResourceBuilder AddDockerContainer(
        this IResourceDeclarationBuilder builder,
        string id,
        string name,
        string image,
        IReadOnlyList<ResourceEndpoint>? endpoints = null) =>
        AddDockerContainerCore(
            builder,
            DockerContainerResourceProvider.EngineResourceId,
            id,
            name,
            image,
            endpoints,
            declareEngine: true);

    internal static IDockerContainerResourceBuilder AddDockerContainerCore(
        IResourceDeclarationBuilder builder,
        string dockerResourceId,
        string id,
        string name,
        string image,
        IReadOnlyList<ResourceEndpoint>? endpoints,
        bool declareEngine)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var definition = new DockerContainerResourceDefinition(
            id,
            name,
            image,
            dockerResourceId,
            endpoints,
            [dockerResourceId]);
        var declared = new DeclaredDockerContainerResource(definition);
        builder.Services
            .GetOrAddDockerProviderOptions()
            .DeclaredContainers
            .Add(declared);

        if (declareEngine)
        {
            builder.AddDockerEngine();
        }

        var resource = builder.Declare(
            "docker",
            definition.Id,
            parentResourceId: definition.DockerResourceId,
            dependsOn: definition.DependsOn,
            onChanged: declaration =>
            {
                declared.Definition = declared.Definition with
                {
                    DependsOn = declaration.DependsOn
                };
                declared.Persist = declaration.Persistence == ResourceDeclarationPersistence.Persisted;
                declared.OverwritePersistedState = declaration.OverwritePersistedState;
            });

        return new DockerContainerResourceBuilder(resource, declared);
    }

    internal static string CreateImageReference(string image, string? tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(image);

        return string.IsNullOrWhiteSpace(tag)
            ? image.Trim()
            : $"{image.Trim()}:{tag.Trim()}";
    }

    private static DockerProviderOptions GetOrAddDockerProviderOptions(
        this IServiceCollection services)
    {
        var options = services
            .Where(descriptor => descriptor.ServiceType == typeof(DockerProviderOptions))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<DockerProviderOptions>()
            .SingleOrDefault();

        if (options is not null)
        {
            return options;
        }

        options = new DockerProviderOptions();
        services.AddSingleton(options);
        return options;
    }
}

public interface IDockerResourceBuilder : IResourceBuilder
{
    IDockerContainerResourceBuilder AddContainer(
        string name,
        string image,
        string? tag = null);

    IDockerContainerResourceBuilder AddDockerContainer(
        string id,
        string name,
        string image,
        IReadOnlyList<ResourceEndpoint>? endpoints = null);

    new IDockerResourceBuilder DependsOn(string resourceId);

    new IDockerResourceBuilder DependsOn(IResourceBuilder resource);

    new IDockerResourceBuilder DependsOn(IEnumerable<string> resourceIds);

    new IDockerResourceBuilder DependsOn(IEnumerable<IResourceBuilder> resources);

    new IDockerResourceBuilder WithResourceGroup(string? resourceGroupId);

    new IDockerResourceBuilder WithParent(string? parentResourceId);

    new IDockerResourceBuilder WithParent(IResourceBuilder resource);

    new IDockerResourceBuilder WithReference(string resourceId);

    new IDockerResourceBuilder WithReference(IResourceBuilder resource);

    new IDockerResourceBuilder WithReferences(IEnumerable<string> resourceIds);

    new IDockerResourceBuilder Persist(bool overwrite = false);
}

internal sealed class DockerResourceBuilder(
    IResourceDeclarationBuilder declarations,
    IResourceBuilder inner) : IDockerResourceBuilder
{
    public ICloudShellBuilder CloudShellBuilder => inner.CloudShellBuilder;

    public string ResourceId => inner.ResourceId;

    public IDockerContainerResourceBuilder AddContainer(
        string name,
        string image,
        string? tag = null) =>
        DockerProviderServiceCollectionExtensions.AddDockerContainerCore(
            declarations,
            ResourceId,
            name,
            name,
            DockerProviderServiceCollectionExtensions.CreateImageReference(image, tag),
            endpoints: null,
            declareEngine: false);

    public IDockerContainerResourceBuilder AddDockerContainer(
        string id,
        string name,
        string image,
        IReadOnlyList<ResourceEndpoint>? endpoints = null) =>
        DockerProviderServiceCollectionExtensions.AddDockerContainerCore(
            declarations,
            ResourceId,
            id,
            name,
            image,
            endpoints,
            declareEngine: false);

    public IDockerResourceBuilder WithResourceGroup(string? resourceGroupId)
    {
        inner.WithResourceGroup(resourceGroupId);
        return this;
    }

    public IDockerResourceBuilder WithParent(string? parentResourceId)
    {
        inner.WithParent(parentResourceId);
        return this;
    }

    public IDockerResourceBuilder WithParent(IResourceBuilder resource)
    {
        inner.WithParent(resource);
        return this;
    }

    public IDockerResourceBuilder DependsOn(string resourceId)
    {
        inner.DependsOn(resourceId);
        return this;
    }

    public IDockerResourceBuilder DependsOn(IResourceBuilder resource)
    {
        inner.DependsOn(resource);
        return this;
    }

    public IDockerResourceBuilder DependsOn(IEnumerable<string> resourceIds)
    {
        inner.DependsOn(resourceIds);
        return this;
    }

    public IDockerResourceBuilder DependsOn(IEnumerable<IResourceBuilder> resources)
    {
        inner.DependsOn(resources);
        return this;
    }

    public IDockerResourceBuilder WithReference(string resourceId)
    {
        inner.WithReference(resourceId);
        return this;
    }

    public IDockerResourceBuilder WithReference(IResourceBuilder resource)
    {
        inner.WithReference(resource);
        return this;
    }

    public IDockerResourceBuilder WithReferences(IEnumerable<string> resourceIds)
    {
        inner.WithReferences(resourceIds);
        return this;
    }

    public IDockerResourceBuilder Persist(bool overwrite = false)
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

public interface IDockerContainerResourceBuilder :
    ILifetimeBoundResourceBuilder<IDockerContainerResourceBuilder>
{
    IDockerContainerResourceBuilder WithImage(string image);

    IDockerContainerResourceBuilder WithEndpoints(IReadOnlyList<ResourceEndpoint> endpoints);

    IDockerContainerResourceBuilder WithEndpoint(ResourceEndpoint endpoint);

    IDockerContainerResourceBuilder WithHttpHealthCheck(
        string path,
        string? endpointName = null,
        string name = "health",
        TimeSpan? timeout = null);

    IDockerContainerResourceBuilder WithHttpProbe(
        ResourceProbeType type,
        string path,
        string? endpointName = null,
        string? name = null,
        TimeSpan? timeout = null);

    new IDockerContainerResourceBuilder DependsOn(IResourceBuilder resource);

    new IDockerContainerResourceBuilder DependsOn(IEnumerable<IResourceBuilder> resources);

    new IDockerContainerResourceBuilder DependsOn(string resourceId);

    new IDockerContainerResourceBuilder DependsOn(IEnumerable<string> resourceIds);

    new IDockerContainerResourceBuilder WithResourceGroup(string? resourceGroupId);

    new IDockerContainerResourceBuilder WithParent(string? parentResourceId);

    new IDockerContainerResourceBuilder WithParent(IResourceBuilder resource);

    new IDockerContainerResourceBuilder WithReference(string resourceId);

    new IDockerContainerResourceBuilder WithReference(IResourceBuilder resource);

    new IDockerContainerResourceBuilder WithReferences(IEnumerable<string> resourceIds);

    new IDockerContainerResourceBuilder Persist(bool overwrite = false);
}

internal sealed class DockerContainerResourceBuilder(
    IResourceBuilder inner,
    DeclaredDockerContainerResource declared) : IDockerContainerResourceBuilder
{
    public ICloudShellBuilder CloudShellBuilder => inner.CloudShellBuilder;

    public string ResourceId => inner.ResourceId;

    public IDockerContainerResourceBuilder WithImage(string image)
    {
        declared.Definition = declared.Definition with { Image = image };
        return this;
    }

    public IDockerContainerResourceBuilder WithLifetime(ResourceLifetime lifetime)
    {
        declared.Definition = declared.Definition with { Lifetime = lifetime };
        return this;
    }

    public IDockerContainerResourceBuilder WithEndpoints(IReadOnlyList<ResourceEndpoint> endpoints)
    {
        declared.Definition = declared.Definition with { Endpoints = endpoints };
        return this;
    }

    public IDockerContainerResourceBuilder WithEndpoint(ResourceEndpoint endpoint)
    {
        declared.Definition = declared.Definition with
        {
            Endpoints = declared.Definition.Endpoints
                .Append(endpoint)
                .ToArray()
        };
        return this;
    }

    public IDockerContainerResourceBuilder WithHttpHealthCheck(
        string path,
        string? endpointName = null,
        string name = "health",
        TimeSpan? timeout = null) =>
        WithHttpProbe(ResourceProbeType.Health, path, endpointName, name, timeout);

    public IDockerContainerResourceBuilder WithHttpProbe(
        ResourceProbeType type,
        string path,
        string? endpointName = null,
        string? name = null,
        TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        declared.Definition = declared.Definition with
        {
            HealthChecks = declared.Definition.HealthChecks
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

    public IDockerContainerResourceBuilder DependsOn(IResourceBuilder resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        inner.WithReference(resource);
        return this;
    }

    public IDockerContainerResourceBuilder DependsOn(IEnumerable<IResourceBuilder> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        inner.WithReferences(resources.Select(resource =>
        {
            ArgumentNullException.ThrowIfNull(resource);
            return resource.ResourceId;
        }));
        return this;
    }

    public IDockerContainerResourceBuilder DependsOn(string resourceId)
    {
        inner.WithReference(resourceId);
        return this;
    }

    public IDockerContainerResourceBuilder DependsOn(IEnumerable<string> resourceIds)
    {
        inner.WithReferences(resourceIds);
        return this;
    }

    public IDockerContainerResourceBuilder WithResourceGroup(string? resourceGroupId)
    {
        inner.WithResourceGroup(resourceGroupId);
        return this;
    }

    public IDockerContainerResourceBuilder WithParent(string? parentResourceId)
    {
        inner.WithParent(parentResourceId);
        return this;
    }

    public IDockerContainerResourceBuilder WithParent(IResourceBuilder resource)
    {
        inner.WithParent(resource);
        return this;
    }

    public IDockerContainerResourceBuilder WithReference(string resourceId)
    {
        inner.WithReference(resourceId);
        return this;
    }

    public IDockerContainerResourceBuilder WithReference(IResourceBuilder resource)
    {
        inner.WithReference(resource);
        return this;
    }

    public IDockerContainerResourceBuilder WithReferences(IEnumerable<string> resourceIds)
    {
        inner.WithReferences(resourceIds);
        return this;
    }

    public IDockerContainerResourceBuilder Persist(bool overwrite = false)
    {
        inner.Persist(overwrite);
        return this;
    }

    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
