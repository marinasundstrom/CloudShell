using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace CloudShell.Providers.Docker;

public static class DockerProviderServiceCollectionExtensions
{
    private const string DefaultOrchestratorId = "default";
    private const string DockerContainerHostId = "docker";

    public static ICloudShellBuilder UseLocalDevelopmentDefaults(
        this ICloudShellBuilder builder,
        Action<DockerProviderOptions>? configure = null)
    {
        UseLocalDevelopmentDefaultsCore(builder, configure);
        return builder;
    }

    public static IControlPlaneBuilder UseLocalDevelopmentDefaults(
        this IControlPlaneBuilder builder,
        Action<DockerProviderOptions>? configure = null)
    {
        UseLocalDevelopmentDefaultsCore(builder, configure);
        return builder;
    }

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

    private static void UseLocalDevelopmentDefaultsCore(
        ICloudShellBuilder builder,
        Action<DockerProviderOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(builder);

        UseDockerCore(builder, configure);
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, LocalDevelopmentDefaultsStartupService>());
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
            ServiceDescriptor.Singleton<IContainerHostProvider, DockerContainerHostProvider>());
    }

    /// <summary>
    /// Declares the default local Docker host resource.
    /// </summary>
    public static IDockerResourceBuilder AddDocker(
        this IResourceDeclarationBuilder builder) =>
        builder.AddDocker(DockerContainerResourceProvider.DefaultHostResourceId);

    /// <summary>
    /// Declares a Docker host resource that can own Docker container
    /// sub-resources.
    /// </summary>
    public static IDockerResourceBuilder AddDocker(
        this IResourceDeclarationBuilder builder,
        string id)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var definition = new DockerResourceDefinition(id, CreateDisplayName(id));
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
                declared.Definition = declared.Definition with
                {
                    Name = GetDisplayName(declaration, CreateDisplayName(id))
                };
                declared.Persist = declaration.Persistence == ResourceDeclarationPersistence.Persisted;
                declared.OverwritePersistedState = declaration.OverwritePersistedState;
            });

        return new DockerResourceBuilder(builder, resource, declared);
    }

    /// <summary>
    /// Declares the default Docker host resource ID without configuring
    /// Docker-specific child-resource builder state.
    /// </summary>
    public static IResourceBuilder AddDockerHost(
        this IResourceDeclarationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Declare("docker", DockerContainerResourceProvider.DefaultHostResourceId);
    }

    /// <summary>
    /// Declares a Docker container sub-resource under the default Docker host
    /// resource.
    /// </summary>
    public static IDockerContainerResourceBuilder AddDockerContainer(
        this IResourceDeclarationBuilder builder,
        string id,
        string image,
        IReadOnlyList<ResourceEndpoint>? endpoints = null) =>
        AddDockerContainerCore(
            builder,
            DockerContainerResourceProvider.DefaultHostResourceId,
            id,
            image,
            endpoints,
            declareHost: true);

    internal static IDockerContainerResourceBuilder AddDockerContainerCore(
        IResourceDeclarationBuilder builder,
        string dockerResourceId,
        string id,
        string image,
        IReadOnlyList<ResourceEndpoint>? endpoints,
        bool declareHost)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var definition = new DockerContainerResourceDefinition(
            id,
            CreateDisplayName(id),
            image,
            dockerResourceId,
            endpoints,
            [dockerResourceId],
            registry: builder.Services.GetOrAddDockerProviderOptions().Registry);
        var declared = new DeclaredDockerContainerResource(definition);
        builder.Services
            .GetOrAddDockerProviderOptions()
            .DeclaredContainers
            .Add(declared);

        if (declareHost)
        {
            builder.AddDockerHost();
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
                    Name = GetDisplayName(declaration, CreateDisplayName(id)),
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

    internal static DockerProviderOptions GetOrAddDockerProviderOptions(
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

    private static string CreateDisplayName(string resourceId)
    {
        var name = resourceId.Contains(':', StringComparison.Ordinal)
            ? resourceId[(resourceId.IndexOf(':', StringComparison.Ordinal) + 1)..]
            : resourceId;
        return string.Join(
            " ",
            name.Split(['-', '_', '.', ':', '/', '\\'], StringSplitOptions.RemoveEmptyEntries)
                .Select(segment => string.Concat(segment[..1].ToUpperInvariant(), segment[1..])));
    }

    private static string GetDisplayName(ResourceDeclaration declaration, string fallback) =>
        string.IsNullOrWhiteSpace(declaration.DisplayName)
            ? fallback
            : declaration.DisplayName;

    private sealed class LocalDevelopmentDefaultsStartupService(
        IServiceProvider serviceProvider) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            var orchestrationSettings = serviceProvider.GetService<IResourceOrchestrationSettings>();
            if (orchestrationSettings is null)
            {
                return Task.CompletedTask;
            }

            var selection = orchestrationSettings.Get();
            if (HasUserSelection(selection))
            {
                return Task.CompletedTask;
            }

            orchestrationSettings.Select(
                DefaultOrchestratorId,
                DockerContainerHostId,
                selection.HealthCheckIntervalSeconds);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private static bool HasUserSelection(ResourceOrchestratorSelection selection) =>
            selection.UpdatedAt != DateTimeOffset.MinValue ||
            !string.Equals(selection.OrchestratorId, DefaultOrchestratorId, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(selection.PreferredContainerHostId);
    }
}

public interface IDockerResourceBuilder : IResourceBuilder
{
    /// <summary>
    /// Configures this Docker resource to use the resolved local Docker host.
    /// </summary>
    IDockerResourceBuilder UseLocalHost();

    /// <summary>
    /// Configures this Docker resource to use a remote Docker host endpoint.
    /// </summary>
    IDockerResourceBuilder UseRemoteHost(Uri endpoint);

    /// <summary>
    /// Sets TLS certificate file references for a remote Docker host.
    /// </summary>
    IDockerResourceBuilder WithTlsCertificateFiles(
        string certificateAuthorityPath,
        string clientCertificatePath,
        string clientKeyPath);

    /// <summary>
    /// Sets username and password environment-variable references for a remote
    /// Docker host.
    /// </summary>
    IDockerResourceBuilder WithHostCredentialsFromEnvironment(
        string username,
        string passwordEnvironmentVariable);

    /// <summary>
    /// Sets the registry used by Docker container resources declared from this
    /// Docker resource. The default registry is
    /// <c>docker.io</c>.
    /// </summary>
    IDockerResourceBuilder WithRegistry(string registry);

    /// <summary>
    /// Sets the registry credentials inherited by Docker container resources
    /// declared from this Docker resource. The password is read from the named
    /// environment variable at execution time.
    /// </summary>
    IDockerResourceBuilder WithRegistryCredentialsFromEnvironment(
        string username,
        string passwordEnvironmentVariable);

    /// <summary>
    /// Declares a Docker container sub-resource parented under this Docker
    /// resource.
    /// </summary>
    /// <remarks>
    /// This is different from standalone container app declarations created with
    /// <c>resources.AddContainer(...)</c> or
    /// <c>resources.AddContainerApplication(...)</c>.
    /// </remarks>
    IDockerContainerResourceBuilder AddContainer(
        string id,
        string image,
        string? tag = null);

    /// <summary>
    /// Declares a Docker container sub-resource with an explicit resource ID.
    /// </summary>
    IDockerContainerResourceBuilder AddDockerContainer(
        string id,
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
    IResourceBuilder inner,
    DeclaredDockerResource declared) : IDockerResourceBuilder
{
    public ICloudShellBuilder CloudShellBuilder => inner.CloudShellBuilder;

    public string ResourceId => inner.ResourceId;

    public ResourceIdentityReference Identity => inner.Identity;

    public IDockerResourceBuilder UseLocalHost()
    {
        declared.Definition = declared.Definition with
        {
            Host = DockerHostDefinition.Local(
                declarations.CloudShellBuilder.Services.GetOrAddDockerProviderOptions().ResolveEndpoint())
        };
        return this;
    }

    public IDockerResourceBuilder UseRemoteHost(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri)
        {
            throw new ArgumentException("Docker host endpoint must be absolute.", nameof(endpoint));
        }

        declared.Definition = declared.Definition with
        {
            Host = DockerHostDefinition.Remote(endpoint)
        };
        return this;
    }

    public IDockerResourceBuilder WithTlsCertificateFiles(
        string certificateAuthorityPath,
        string clientCertificatePath,
        string clientKeyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(certificateAuthorityPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientCertificatePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientKeyPath);
        var currentHost = declared.Definition.Host ?? DockerHostDefinition.Local(
            declarations.CloudShellBuilder.Services.GetOrAddDockerProviderOptions().ResolveEndpoint());
        declared.Definition = declared.Definition with
        {
            Host = currentHost with
            {
                Credentials = new DockerHostCredentials(
                    DockerHostCredentialKind.TlsCertificateFiles,
                    CertificateAuthorityPath: certificateAuthorityPath.Trim(),
                    ClientCertificatePath: clientCertificatePath.Trim(),
                    ClientKeyPath: clientKeyPath.Trim())
            }
        };
        return this;
    }

    public IDockerResourceBuilder WithHostCredentialsFromEnvironment(
        string username,
        string passwordEnvironmentVariable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordEnvironmentVariable);
        var currentHost = declared.Definition.Host ?? DockerHostDefinition.Local(
            declarations.CloudShellBuilder.Services.GetOrAddDockerProviderOptions().ResolveEndpoint());
        declared.Definition = declared.Definition with
        {
            Host = currentHost with
            {
                Credentials = new DockerHostCredentials(
                    DockerHostCredentialKind.UsernamePasswordEnvironmentVariable,
                    Username: username.Trim(),
                    PasswordEnvironmentVariable: passwordEnvironmentVariable.Trim())
            }
        };
        return this;
    }

    public IDockerContainerResourceBuilder AddContainer(
        string id,
        string image,
        string? tag = null)
    {
        var container = DockerProviderServiceCollectionExtensions.AddDockerContainerCore(
            declarations,
            ResourceId,
            id,
            DockerProviderServiceCollectionExtensions.CreateImageReference(image, tag),
            endpoints: null,
            declareHost: false)
            .WithRegistry(declared.Definition.Registry);
        return InheritRegistryCredentials(container);
    }

    public IDockerContainerResourceBuilder AddDockerContainer(
        string id,
        string image,
        IReadOnlyList<ResourceEndpoint>? endpoints = null)
    {
        var container = DockerProviderServiceCollectionExtensions.AddDockerContainerCore(
            declarations,
            ResourceId,
            id,
            image,
            endpoints,
            declareHost: false)
            .WithRegistry(declared.Definition.Registry);
        return InheritRegistryCredentials(container);
    }

    public IDockerResourceBuilder WithRegistry(string registry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registry);
        declared.Definition = declared.Definition with
        {
            Registry = registry.Trim()
        };
        return this;
    }

    public IDockerResourceBuilder WithRegistryCredentialsFromEnvironment(
        string username,
        string passwordEnvironmentVariable)
    {
        declared.Definition = declared.Definition with
        {
            RegistryCredentials = ContainerRegistryCredentials.Normalize(
                new ContainerRegistryCredentials(username, passwordEnvironmentVariable))
        };
        return this;
    }

    private IDockerContainerResourceBuilder InheritRegistryCredentials(IDockerContainerResourceBuilder container)
    {
        var credentials = declared.Definition.RegistryCredentials;
        if (credentials is null)
        {
            return container;
        }

        return container.WithRegistryCredentialsFromEnvironment(
            credentials.Username,
            credentials.PasswordEnvironmentVariable);
    }

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

    /// <summary>
    /// Sets the registry for this Docker container resource. The default
    /// registry is inherited from the parent Docker resource.
    /// </summary>
    IDockerContainerResourceBuilder WithRegistry(string registry);

    /// <summary>
    /// Sets the registry credentials for this Docker container resource. The
    /// password is read from the named environment variable at execution time.
    /// </summary>
    IDockerContainerResourceBuilder WithRegistryCredentialsFromEnvironment(
        string username,
        string passwordEnvironmentVariable);

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

    public ResourceIdentityReference Identity => inner.Identity;

    public IDockerContainerResourceBuilder WithImage(string image)
    {
        declared.Definition = declared.Definition with { Image = image };
        return this;
    }

    public IDockerContainerResourceBuilder WithRegistry(string registry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registry);
        declared.Definition = declared.Definition with { Registry = registry.Trim() };
        return this;
    }

    public IDockerContainerResourceBuilder WithRegistryCredentialsFromEnvironment(
        string username,
        string passwordEnvironmentVariable)
    {
        return WithRegistryCredentials(
            ContainerRegistryCredentials.Normalize(
                new ContainerRegistryCredentials(username, passwordEnvironmentVariable)));
    }

    private IDockerContainerResourceBuilder WithRegistryCredentials(ContainerRegistryCredentials? credentials)
    {
        declared.Definition = declared.Definition with { RegistryCredentials = credentials };
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
