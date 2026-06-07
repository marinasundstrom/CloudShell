using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.Providers.Docker;

public static class DockerProviderServiceCollectionExtensions
{
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
        var options = builder.Services.GetOrAddDockerProviderOptions();
        configure?.Invoke(options);
    }

    public static IDockerResourceBuilder AddDocker(
        this ICloudShellResourceDeclarationBuilder builder) =>
        builder.AddDocker(DockerContainerResourceProvider.EngineResourceId, "Local Docker Engine");

    public static IDockerResourceBuilder AddDocker(
        this ICloudShellResourceDeclarationBuilder builder,
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

    public static ICloudShellResourceBuilder AddDockerEngine(
        this ICloudShellResourceDeclarationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Declare("docker", DockerContainerResourceProvider.EngineResourceId);
    }

    public static IDockerContainerResourceBuilder AddContainer(
        this ICloudShellResourceDeclarationBuilder builder,
        string name,
        string image,
        string? tag = null) =>
        builder.AddDockerContainer(
            name,
            name,
            CreateImageReference(image, tag));

    public static IDockerContainerResourceBuilder AddDockerContainer(
        this ICloudShellResourceDeclarationBuilder builder,
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
        ICloudShellResourceDeclarationBuilder builder,
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

public interface IDockerResourceBuilder : ICloudShellResourceBuilder
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

    new IDockerResourceBuilder WithResourceGroup(string? resourceGroupId);

    new IDockerResourceBuilder WithReference(string resourceId);

    new IDockerResourceBuilder WithReference(ICloudShellResourceBuilder resource);

    new IDockerResourceBuilder WithReferences(IEnumerable<string> resourceIds);

    new IDockerResourceBuilder Persist(bool overwrite = false);
}

internal sealed class DockerResourceBuilder(
    ICloudShellResourceDeclarationBuilder declarations,
    ICloudShellResourceBuilder inner) : IDockerResourceBuilder
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

    public IDockerResourceBuilder WithReference(string resourceId)
    {
        inner.WithReference(resourceId);
        return this;
    }

    public IDockerResourceBuilder WithReference(ICloudShellResourceBuilder resource)
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

    ICloudShellResourceBuilder ICloudShellResourceBuilder.WithResourceGroup(string? resourceGroupId) =>
        WithResourceGroup(resourceGroupId);

    ICloudShellResourceBuilder ICloudShellResourceBuilder.WithReference(string resourceId) =>
        WithReference(resourceId);

    ICloudShellResourceBuilder ICloudShellResourceBuilder.WithReference(ICloudShellResourceBuilder resource) =>
        WithReference(resource);

    ICloudShellResourceBuilder ICloudShellResourceBuilder.WithReferences(IEnumerable<string> resourceIds) =>
        WithReferences(resourceIds);

    ICloudShellResourceBuilder ICloudShellResourceBuilder.Persist(bool overwrite) =>
        Persist(overwrite);
}

public interface IDockerContainerResourceBuilder : ICloudShellResourceBuilder
{
    IDockerContainerResourceBuilder WithImage(string image);

    IDockerContainerResourceBuilder WithEndpoints(IReadOnlyList<ResourceEndpoint> endpoints);

    IDockerContainerResourceBuilder WithEndpoint(ResourceEndpoint endpoint);

    IDockerContainerResourceBuilder DependsOn(ICloudShellResourceBuilder resource);

    IDockerContainerResourceBuilder DependsOn(IEnumerable<ICloudShellResourceBuilder> resources);

    IDockerContainerResourceBuilder DependsOn(string resourceId);

    IDockerContainerResourceBuilder DependsOn(IEnumerable<string> resourceIds);

    new IDockerContainerResourceBuilder WithResourceGroup(string? resourceGroupId);

    new IDockerContainerResourceBuilder WithReference(string resourceId);

    new IDockerContainerResourceBuilder WithReference(ICloudShellResourceBuilder resource);

    new IDockerContainerResourceBuilder WithReferences(IEnumerable<string> resourceIds);

    new IDockerContainerResourceBuilder Persist(bool overwrite = false);
}

internal sealed class DockerContainerResourceBuilder(
    ICloudShellResourceBuilder inner,
    DeclaredDockerContainerResource declared) : IDockerContainerResourceBuilder
{
    public ICloudShellBuilder CloudShellBuilder => inner.CloudShellBuilder;

    public string ResourceId => inner.ResourceId;

    public IDockerContainerResourceBuilder WithImage(string image)
    {
        declared.Definition = declared.Definition with { Image = image };
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

    public IDockerContainerResourceBuilder DependsOn(ICloudShellResourceBuilder resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        inner.WithReference(resource);
        return this;
    }

    public IDockerContainerResourceBuilder DependsOn(IEnumerable<ICloudShellResourceBuilder> resources)
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

    public IDockerContainerResourceBuilder WithReference(string resourceId)
    {
        inner.WithReference(resourceId);
        return this;
    }

    public IDockerContainerResourceBuilder WithReference(ICloudShellResourceBuilder resource)
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

    ICloudShellResourceBuilder ICloudShellResourceBuilder.WithResourceGroup(string? resourceGroupId) =>
        WithResourceGroup(resourceGroupId);

    ICloudShellResourceBuilder ICloudShellResourceBuilder.WithReference(string resourceId) =>
        WithReference(resourceId);

    ICloudShellResourceBuilder ICloudShellResourceBuilder.WithReference(ICloudShellResourceBuilder resource) =>
        WithReference(resource);

    ICloudShellResourceBuilder ICloudShellResourceBuilder.WithReferences(IEnumerable<string> resourceIds) =>
        WithReferences(resourceIds);

    ICloudShellResourceBuilder ICloudShellResourceBuilder.Persist(bool overwrite) =>
        Persist(overwrite);
}
