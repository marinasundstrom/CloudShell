using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.Providers.Applications;

public static class ApplicationProviderServiceCollectionExtensions
{
    public static ICloudShellBuilder AddApplicationProvider(
        this ICloudShellBuilder builder,
        Action<ApplicationProviderOptions>? configure = null,
        CloudShellExtensionActivationPolicy activationPolicy = CloudShellExtensionActivationPolicy.Enabled)
    {
        AddApplicationProviderCore(builder, configure);
        return builder.AddExtension(new ApplicationProviderExtension(), activationPolicy);
    }

    public static IControlPlaneBuilder AddApplicationProvider(
        this IControlPlaneBuilder builder,
        Action<ApplicationProviderOptions>? configure = null,
        CloudShellExtensionActivationPolicy activationPolicy = CloudShellExtensionActivationPolicy.Enabled)
    {
        AddApplicationProviderCore(builder, configure);
        return builder.AddExtension(new ApplicationProviderExtension(), activationPolicy);
    }

    private static void AddApplicationProviderCore(
        ICloudShellBuilder builder,
        Action<ApplicationProviderOptions>? configure)
    {
        var options = builder.Services.GetOrAddApplicationProviderOptions();
        configure?.Invoke(options);
        builder.Services.AddLocalProcessRunner(processes =>
        {
            processes.RuntimeStatePath = options.RuntimeStatePath;
            processes.LogDirectory = options.LogDirectory;
        });
    }

    public static IExecutableResourceBuilder AddExecutable(
        this IResourceDeclarationBuilder builder,
        string name) =>
        builder.AddExecutableApplication(name, executablePath: string.Empty);

    public static IExecutableResourceBuilder AddExecutableApplication(
        this IResourceDeclarationBuilder builder,
        string name,
        string executablePath,
        string? arguments = null,
        string? workingDirectory = null,
        string? endpoint = null,
        IReadOnlyList<EnvironmentVariableAssignment>? environmentVariables = null,
        ApplicationLifetime lifetime = ApplicationLifetime.ControlPlaneScoped,
        bool useServiceDiscovery = false,
        ResourceObservability? observability = null)
    {
        var id = CreateApplicationResourceId(name);
        var definition = new ApplicationResourceDefinition(
            id,
            CreateDisplayName(id),
            executablePath,
            arguments,
            workingDirectory,
            endpoint,
            environmentVariables,
            lifetime,
            useServiceDiscovery: useServiceDiscovery,
            observability: observability);
        var declared = new DeclaredApplicationResource(definition);

        builder.Services
            .GetOrAddApplicationProviderOptions()
            .DeclaredApplications
            .Add(declared);

        var resource = builder.Declare(
            "applications",
            id,
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

        return new ExecutableApplicationResourceBuilder(resource, declared);
    }

    public static IProjectResourceBuilder AddAspNetCoreProject(
        this IResourceDeclarationBuilder builder,
        string name,
        string projectPath,
        string? endpoint = null,
        IReadOnlyList<EnvironmentVariableAssignment>? environmentVariables = null,
        ApplicationLifetime lifetime = ApplicationLifetime.ControlPlaneScoped,
        bool hotReload = false,
        bool useServiceDiscovery = false,
        ResourceObservability? observability = null,
        string? applicationArguments = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);

        var id = CreateApplicationResourceId(name);
        var definition = new ApplicationResourceDefinition(
            id,
            CreateDisplayName(id),
            executablePath: string.Empty,
            environmentVariables: environmentVariables,
            lifetime: lifetime,
            useServiceDiscovery: useServiceDiscovery,
            endpointPorts: string.IsNullOrWhiteSpace(endpoint)
                ? []
                : CreateAspNetCoreProjectEndpointPorts(endpoint),
            resourceType: ApplicationResourceTypes.AspNetCoreProject,
            observability: observability,
            projectPath: projectPath,
            projectArguments: applicationArguments,
            aspNetCoreHotReload: hotReload);
        var declared = new DeclaredApplicationResource(definition);

        builder.Services
            .GetOrAddApplicationProviderOptions()
            .DeclaredApplications
            .Add(declared);

        var resource = builder.Declare(
            "applications",
            id,
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

        return new ExecutableApplicationResourceBuilder(resource, declared);
    }

    public static IProjectResourceBuilder AsContainer(
        this IProjectResourceBuilder builder,
        string? image = null,
        string? buildContext = null,
        string? dockerfile = null,
        string? registry = null,
        int? replicas = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(image))
        {
            if (builder is IProjectContainerBuildResourceBuilder containerBuildBuilder)
            {
                containerBuildBuilder.AsProjectContainerBuild(buildContext, dockerfile);
            }
            else
            {
                builder.WithContainerBuild(buildContext, dockerfile);
            }
        }
        else
        {
            builder.AsContainerImage(image);
        }

        if (!string.IsNullOrWhiteSpace(registry))
        {
            builder.WithRegistry(registry);
        }

        if (replicas.HasValue)
        {
            builder.WithReplicas(replicas.Value);
        }

        return builder;
    }

    public static IProjectResourceBuilder WithContainerHost(
        this IProjectResourceBuilder builder,
        string containerHostId)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerHostId);

        if (builder is not IContainerResourceBuilder containerBuilder)
        {
            throw new InvalidOperationException(
                "This project resource builder does not support container host binding.");
        }

        containerBuilder.WithContainerHost(containerHostId);
        return builder;
    }

    public static IProjectResourceBuilder WithContainerHost(
        this IProjectResourceBuilder builder,
        IResourceBuilder containerHost)
    {
        ArgumentNullException.ThrowIfNull(containerHost);
        return builder.WithContainerHost(containerHost.ResourceId);
    }

    /// <summary>
    /// Declares a container app resource using an Aspire-compatible shorthand
    /// name.
    /// </summary>
    /// <remarks>
    /// This creates an <c>application.container-app</c> resource. It does not
    /// create a Docker container sub-resource. Use
    /// <c>resources.AddDocker().AddContainer(...)</c> when Docker itself should
    /// be modeled as the parent resource.
    /// </remarks>
    public static IContainerResourceBuilder AddContainer(
        this IResourceDeclarationBuilder builder,
        string name,
        string image,
        IReadOnlyList<ResourceEndpoint>? endpoints = null,
        IReadOnlyList<EnvironmentVariableAssignment>? environmentVariables = null,
        bool useServiceDiscovery = false,
        int replicas = 1,
        ResourceObservability? observability = null,
        string? registry = null) =>
        builder.AddContainerApplication(
            name,
            image,
            endpoints,
            environmentVariables,
            useServiceDiscovery,
            replicas,
            observability,
            registry);

    /// <summary>
    /// Declares a standalone container app resource with a scoped resource
    /// name.
    /// </summary>
    /// <remarks>
    /// The declared resource is the stable deployment target for image updates
    /// and revisions. Runtime containers or replicas may be projected by the
    /// selected container host provider, but callers should deploy through the
    /// container app resource.
    /// </remarks>
    public static IContainerResourceBuilder AddContainerApplication(
        this IResourceDeclarationBuilder builder,
        string name,
        string image,
        IReadOnlyList<ResourceEndpoint>? endpoints = null,
        IReadOnlyList<EnvironmentVariableAssignment>? environmentVariables = null,
        bool useServiceDiscovery = false,
        int replicas = 1,
        ResourceObservability? observability = null,
        string? registry = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(image);

        var id = CreateApplicationResourceId(name);
        var endpoint = endpoints?.FirstOrDefault(endpoint =>
            !string.IsNullOrWhiteSpace(endpoint.Address))?.Address;
        var definition = new ApplicationResourceDefinition(
            id,
            CreateDisplayName(id),
            executablePath: string.Empty,
            endpoint: endpoint,
            environmentVariables: environmentVariables,
            lifetime: ApplicationLifetime.ControlPlaneScoped,
            useServiceDiscovery: useServiceDiscovery,
            containerImage: image,
            containerRegistry: registry,
            replicas: Math.Max(1, replicas),
            replicasEnabled: replicas > 1,
            endpointPorts: CreateEndpointPorts(endpoints),
            resourceType: ApplicationResourceTypes.ContainerApp,
            observability: observability,
            containerRevision: CreateContainerRevision());
        var declared = new DeclaredApplicationResource(definition);

        builder.Services
            .GetOrAddApplicationProviderOptions()
            .DeclaredApplications
            .Add(declared);

        var resource = builder.Declare(
            "applications",
            id,
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

        return new ExecutableApplicationResourceBuilder(resource, declared);
    }

    private static string CreateApplicationResourceId(string name)
    {
        return ResourceId.FromName("application", name).Value;
    }

    private static string CreateDisplayName(string resourceId)
        => ResourceId.TryParse(resourceId, out var id)
            ? id.Name
            : resourceId.Trim();

    private static string GetDisplayName(ResourceDeclaration declaration, string fallback) =>
        string.IsNullOrWhiteSpace(declaration.DisplayName)
            ? fallback
            : declaration.DisplayName;

    private static string CreateContainerRevision() =>
        $"rev-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..27];

    internal static IReadOnlyList<ServicePort> CreateAspNetCoreProjectEndpointPorts(string? endpoint)
    {
        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) &&
            uri.Port > 0)
        {
            return
            [
                new ServicePort(
                    "http",
                    uri.Port,
                    uri.Port,
                    string.IsNullOrWhiteSpace(uri.Scheme) ? "http" : uri.Scheme,
                    ResourceExposureScope.Local,
                    ResourceEndpointAssignment.Manual,
                    Host: uri.Host)
            ];
        }

        return [new ServicePort("http", 80, Protocol: "http", Exposure: ResourceExposureScope.Local)];
    }

    private static IReadOnlyList<ServicePort> CreateEndpointPorts(
        IReadOnlyList<ResourceEndpoint>? endpoints) =>
        endpoints?
            .Select(TryCreateEndpointPort)
            .Where(port => port is not null)
            .Select(port => port!)
            .ToArray()
        ?? [];

    private static ServicePort? TryCreateEndpointPort(ResourceEndpoint endpoint)
    {
        var hasAddress = Uri.TryCreate(endpoint.Address, UriKind.Absolute, out var uri) &&
            uri.Port > 0;
        var targetPort = endpoint.TargetPort ?? (hasAddress ? uri!.Port : null);
        if (targetPort is null)
        {
            return null;
        }

        return new ServicePort(
            string.IsNullOrWhiteSpace(endpoint.Name) ? "default" : endpoint.Name,
            targetPort.Value,
            hasAddress ? uri!.Port : null,
            string.IsNullOrWhiteSpace(endpoint.Protocol)
                ? hasAddress ? uri!.Scheme : "tcp"
                : endpoint.Protocol,
            endpoint.Exposure,
            hasAddress ? ResourceEndpointAssignment.Manual : ResourceEndpointAssignment.ProviderDefault,
            Host: hasAddress ? uri!.Host : null);
    }

    private static ApplicationProviderOptions GetOrAddApplicationProviderOptions(
        this IServiceCollection services)
    {
        var options = services
            .Where(descriptor => descriptor.ServiceType == typeof(ApplicationProviderOptions))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<ApplicationProviderOptions>()
            .SingleOrDefault();

        if (options is not null)
        {
            return options;
        }

        options = new ApplicationProviderOptions();
        services.AddSingleton(options);
        return options;
    }
}

internal interface IProjectContainerBuildResourceBuilder
{
    IProjectResourceBuilder AsProjectContainerBuild(
        string? buildContext,
        string? dockerfile = null);
}

internal sealed class ExecutableApplicationResourceBuilder(
    IResourceBuilder inner,
    DeclaredApplicationResource declared) :
    IExecutableResourceBuilder,
    IProjectResourceBuilder,
    IContainerResourceBuilder,
    IProjectContainerBuildResourceBuilder
{
    public ICloudShellBuilder CloudShellBuilder => inner.CloudShellBuilder;

    public string ResourceId => inner.ResourceId;

    public ResourceIdentityReference Identity => inner.Identity;

    public IExecutableResourceBuilder WithCommand(
        string executablePath,
        string? arguments = null,
        string? workingDirectory = null)
    {
        declared.Definition = declared.Definition with
        {
            ExecutablePath = executablePath,
            Arguments = arguments,
            WorkingDirectory = workingDirectory
        };
        return this;
    }

    public IExecutableResourceBuilder WithEndpoint(string? endpoint)
    {
        if (string.Equals(
                declared.Definition.ResourceType,
                ApplicationResourceTypes.AspNetCoreProject,
                StringComparison.OrdinalIgnoreCase))
        {
            declared.Definition = declared.Definition with
            {
                Endpoint = null,
                EndpointPorts = ApplicationProviderServiceCollectionExtensions.CreateAspNetCoreProjectEndpointPorts(endpoint),
                UseLaunchSettingsEndpoints = false
            };
            return this;
        }

        declared.Definition = declared.Definition with
        {
            Endpoint = endpoint
        };
        return this;
    }

    public IExecutableResourceBuilder WithEndpointPort(
        string name,
        int targetPort,
        int? port = null,
        string protocol = "http",
        ResourceExposureScope exposure = ResourceExposureScope.Local)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        declared.Definition = declared.Definition with
        {
            Endpoint = null,
            UseLaunchSettingsEndpoints = false,
            EndpointPorts = declared.Definition.EndpointPorts
                .Where(endpoint => !string.Equals(endpoint.Name, name, StringComparison.OrdinalIgnoreCase))
                .Append(CreateDeclaredServicePort(name, targetPort, port, protocol, exposure))
                .ToArray()
        };
        return this;
    }

    public IExecutableResourceBuilder WithHttpEndpoint(
        int? port = null,
        int targetPort = 80,
        string name = "http") =>
        WithEndpointPort(name, targetPort, port, "http");

    public IExecutableResourceBuilder WithHttpsEndpoint(
        int? port = null,
        int targetPort = 443,
        string name = "https") =>
        WithEndpointPort(name, targetPort, port, "https");

    public IProjectResourceBuilder WithLaunchSettingsEndpoints(bool enabled = true)
    {
        declared.Definition = declared.Definition with
        {
            UseLaunchSettingsEndpoints = enabled
        };
        return this;
    }

    public IExecutableResourceBuilder WithHttpHealthCheck(
        string path,
        string? endpointName = null,
        string name = "health",
        TimeSpan? timeout = null) =>
        WithHttpProbe(ResourceProbeType.Health, path, endpointName, name, timeout);

    public IExecutableResourceBuilder WithHttpProbe(
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

    public IExecutableResourceBuilder WithEnvironment(
        IReadOnlyList<EnvironmentVariableAssignment> environmentVariables)
    {
        declared.Definition = declared.Definition with
        {
            EnvironmentVariables = environmentVariables
        };
        return this;
    }

    public IExecutableResourceBuilder WithEnvironment(
        string name,
        string value)
    {
        declared.Definition = declared.Definition with
        {
            EnvironmentVariables = declared.Definition.EnvironmentVariables
                .Append(new EnvironmentVariableAssignment(name, value))
                .ToArray()
        };
        return this;
    }

    public IExecutableResourceBuilder WithEnvironment(
        string name,
        ConfigurationEntryReference configurationEntry)
    {
        ArgumentNullException.ThrowIfNull(configurationEntry);
        declared.Definition = declared.Definition with
        {
            EnvironmentVariables = declared.Definition.EnvironmentVariables
                .Append(EnvironmentVariableAssignment.FromConfiguration(name, configurationEntry))
                .ToArray()
        };
        inner.DependsOn(configurationEntry.StoreResourceId);
        return this;
    }

    public IExecutableResourceBuilder WithEnvironment(
        string name,
        SecretReference secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        declared.Definition = declared.Definition with
        {
            EnvironmentVariables = declared.Definition.EnvironmentVariables
                .Append(EnvironmentVariableAssignment.FromSecret(name, secret))
                .ToArray()
        };
        inner.DependsOn(secret.VaultResourceId);
        return this;
    }

    public IExecutableResourceBuilder WithAppSetting(
        string name,
        string value)
    {
        declared.Definition = declared.Definition with
        {
            AppSettings = declared.Definition.AppSettings
                .Append(AppSetting.Literal(name, value))
                .ToArray()
        };
        return this;
    }

    public IExecutableResourceBuilder WithAppSetting(
        string name,
        ConfigurationEntryReference configurationEntry)
    {
        ArgumentNullException.ThrowIfNull(configurationEntry);
        declared.Definition = declared.Definition with
        {
            AppSettings = declared.Definition.AppSettings
                .Append(AppSetting.FromConfiguration(name, configurationEntry))
                .ToArray()
        };
        inner.DependsOn(configurationEntry.StoreResourceId);
        return this;
    }

    public IExecutableResourceBuilder WithAppSetting(
        string name,
        SecretReference secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        declared.Definition = declared.Definition with
        {
            AppSettings = declared.Definition.AppSettings
                .Append(AppSetting.FromSecret(name, secret))
                .ToArray()
        };
        inner.DependsOn(secret.VaultResourceId);
        return this;
    }

    public IProjectResourceBuilder WithApplicationArguments(string? arguments)
    {
        declared.Definition = declared.Definition with
        {
            ProjectArguments = NormalizeNullable(arguments)
        };
        return this;
    }

    public IExecutableResourceBuilder WithLifetime(ApplicationLifetime lifetime)
    {
        declared.Definition = declared.Definition with { Lifetime = lifetime };
        return this;
    }

    public IExecutableResourceBuilder WithServiceDiscovery(bool enabled = true)
    {
        declared.Definition = declared.Definition with
        {
            UseServiceDiscovery = enabled
        };
        return this;
    }

    public IExecutableResourceBuilder WithObservability(bool enabled = true)
    {
        declared.Definition = declared.Definition with
        {
            Observability = enabled
                ? GetCurrentObservability() with
                {
                    Logs = true,
                    Traces = true,
                    Metrics = true
                }
                : ResourceObservability.None
        };
        return this;
    }

    public IExecutableResourceBuilder WithOtlpExporter(
        string? endpoint = null,
        string? protocol = null,
        string? headers = null)
    {
        declared.Definition = declared.Definition with
        {
            Observability = GetCurrentObservability() with
            {
                Logs = true,
                Traces = true,
                Metrics = true,
                OtlpEndpoint = NormalizeNullable(endpoint),
                OtlpProtocol = NormalizeNullable(protocol),
                OtlpHeaders = NormalizeNullable(headers)
            }
        };
        return this;
    }

    public IProjectResourceBuilder AsContainerImage(string image)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(image);
        return WithContainerImage(image);
    }

    public IProjectResourceBuilder WithRegistry(string registry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registry);
        declared.Definition = declared.Definition with
        {
            ContainerRegistry = registry.Trim()
        };
        return this;
    }

    public IProjectResourceBuilder WithRegistryCredentialsFromEnvironment(
        string username,
        string passwordEnvironmentVariable)
    {
        declared.Definition = declared.Definition with
        {
            ContainerRegistryCredentials = ContainerRegistryCredentials.Normalize(
                new ContainerRegistryCredentials(username, passwordEnvironmentVariable))
        };
        return this;
    }

    public IProjectResourceBuilder WithContainerImage(string? image)
    {
        declared.Definition = declared.Definition with
        {
            ContainerImage = image,
            ContainerBuildContext = null,
            ContainerDockerfile = null,
            ProjectContainerBuild = false
        };
        return this;
    }

    public IContainerResourceBuilder WithImage(string image)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(image);
        WithContainerImage(image);
        return this;
    }

    IContainerResourceBuilder IContainerResourceBuilder.WithRegistry(string registry)
    {
        WithRegistry(registry);
        return this;
    }

    IContainerResourceBuilder IContainerResourceBuilder.WithRegistryCredentialsFromEnvironment(
        string username,
        string passwordEnvironmentVariable)
    {
        WithRegistryCredentialsFromEnvironment(username, passwordEnvironmentVariable);
        return this;
    }

    public IContainerResourceBuilder WithContainerHost(string containerHostId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerHostId);
        declared.Definition = declared.Definition with
        {
            ContainerHostId = containerHostId
        };
        return this;
    }

    public IContainerResourceBuilder WithContainerHost(IResourceBuilder containerEngine)
    {
        ArgumentNullException.ThrowIfNull(containerEngine);
        return WithContainerHost(containerEngine.ResourceId);
    }

    public IContainerResourceBuilder WithEndpoint(
        string name,
        int targetPort,
        int? port = null,
        string protocol = "tcp",
        ResourceExposureScope exposure = ResourceExposureScope.Local)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        declared.Definition = declared.Definition with
        {
            EndpointPorts = declared.Definition.EndpointPorts
                .Append(CreateDeclaredServicePort(name, targetPort, port, protocol, exposure))
                .ToArray()
        };
        return this;
    }

    public IContainerResourceBuilder WithVolume(
        string volumeReference,
        string targetPath,
        bool readOnly = false,
        string? name = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(volumeReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        declared.Definition = declared.Definition with
        {
            VolumeMounts = declared.Definition.VolumeMounts
                .Append(new ResourceVolumeMount(volumeReference, targetPath, readOnly, name))
                .ToArray()
        };
        return this;
    }

    public IContainerResourceBuilder WithVolume(
        IResourceBuilder volume,
        string targetPath,
        bool readOnly = false,
        string? name = null)
    {
        ArgumentNullException.ThrowIfNull(volume);
        DependsOn(volume);
        return WithVolume(volume.ResourceId, targetPath, readOnly, name);
    }

    public IProjectResourceBuilder WithContainerBuild(
        string? buildContext,
        string? dockerfile = null)
    {
        declared.Definition = declared.Definition with
        {
            ContainerBuildContext = buildContext,
            ContainerDockerfile = dockerfile,
            ContainerImage = null,
            ProjectContainerBuild = false
        };
        return this;
    }

    public IProjectResourceBuilder WithDockerBuild(
        string? buildContext,
        string? dockerfile = null) =>
        WithContainerBuild(buildContext, dockerfile);

    public IProjectResourceBuilder AsProjectContainerBuild(
        string? buildContext,
        string? dockerfile = null)
    {
        if (!string.Equals(
                declared.Definition.ResourceType,
                ApplicationResourceTypes.AspNetCoreProject,
                StringComparison.OrdinalIgnoreCase))
        {
            return WithContainerBuild(buildContext, dockerfile);
        }

        declared.Definition = declared.Definition with
        {
            ContainerBuildContext = NormalizeNullable(buildContext),
            ContainerDockerfile = NormalizeNullable(dockerfile),
            ContainerImage = null,
            ProjectContainerBuild = true,
            ResourceType = ApplicationResourceTypes.ContainerApp
        };
        return this;
    }

    public IProjectResourceBuilder WithReplicas(int replicas)
    {
        declared.Definition = declared.Definition with
        {
            Replicas = Math.Max(1, replicas),
            ReplicasEnabled = true
        };
        return this;
    }

    public IContainerResourceBuilder WithLifetime(ResourceLifetime lifetime)
    {
        declared.Definition = declared.Definition with
        {
            Lifetime = ToApplicationLifetime(lifetime)
        };
        return this;
    }

    IExecutableResourceBuilder IExecutableResourceBuilder.WithLifetime(ResourceLifetime lifetime)
    {
        declared.Definition = declared.Definition with
        {
            Lifetime = ToApplicationLifetime(lifetime)
        };
        return this;
    }

    IExecutableResourceBuilder ILifetimeBoundResourceBuilder<IExecutableResourceBuilder>.WithLifetime(
        ResourceLifetime lifetime)
    {
        declared.Definition = declared.Definition with
        {
            Lifetime = ToApplicationLifetime(lifetime)
        };
        return this;
    }

    public IExecutableResourceBuilder WithResourceGroup(string? resourceGroupId)
    {
        inner.WithResourceGroup(resourceGroupId);
        return this;
    }

    public IExecutableResourceBuilder WithParent(string? parentResourceId)
    {
        inner.WithParent(parentResourceId);
        return this;
    }

    public IExecutableResourceBuilder WithParent(IResourceBuilder resource)
    {
        inner.WithParent(resource);
        return this;
    }

    public IExecutableResourceBuilder WithReference(IResourceBuilder resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        declared.Definition = declared.Definition with
        {
            References = declared.Definition.References
                .Append(resource.ResourceId)
                .ToArray(),
            UseServiceDiscovery = ShouldEnableServiceDiscoveryOnReference()
                ? true
                : declared.Definition.UseServiceDiscovery
        };
        return this;
    }

    public IExecutableResourceBuilder WithReferences(IEnumerable<IResourceBuilder> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        var resourceIds = resources
            .Select(resource =>
            {
                ArgumentNullException.ThrowIfNull(resource);
                return resource.ResourceId;
            })
            .ToArray();

        declared.Definition = declared.Definition with
        {
            References = declared.Definition.References
                .Concat(resourceIds)
                .ToArray(),
            UseServiceDiscovery = ShouldEnableServiceDiscoveryOnReference()
                ? true
                : declared.Definition.UseServiceDiscovery
        };
        return this;
    }

    public IExecutableResourceBuilder WaitFor(IResourceBuilder resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return DependsOn(resource);
    }

    public IExecutableResourceBuilder WaitFor(IEnumerable<IResourceBuilder> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        return DependsOn(resources);
    }

    public IExecutableResourceBuilder DependsOn(string resourceId)
    {
        inner.DependsOn(resourceId);
        return this;
    }

    public IExecutableResourceBuilder DependsOn(IResourceBuilder resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        inner.DependsOn(resource);
        return this;
    }

    public IExecutableResourceBuilder DependsOn(IEnumerable<string> resourceIds)
    {
        inner.DependsOn(resourceIds);
        return this;
    }

    public IExecutableResourceBuilder DependsOn(IEnumerable<IResourceBuilder> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        inner.DependsOn(resources.Select(resource =>
        {
            ArgumentNullException.ThrowIfNull(resource);
            return resource.ResourceId;
        }));
        return this;
    }

    public IExecutableResourceBuilder Persist(bool overwrite = false)
    {
        inner.Persist(overwrite);
        return this;
    }

    private static ServicePort CreateDeclaredServicePort(
        string name,
        int targetPort,
        int? port,
        string protocol,
        ResourceExposureScope exposure) =>
        new(
            name,
            targetPort,
            port,
            protocol,
            exposure,
            port is null
                ? ResourceEndpointAssignment.Auto
                : ResourceEndpointAssignment.Manual);

    IProjectResourceBuilder IProjectResourceBuilder.WithEndpoint(string? endpoint)
    {
        WithEndpoint(endpoint);
        return this;
    }

    IProjectResourceBuilder IProjectResourceBuilder.WithEndpointPort(
        string name,
        int targetPort,
        int? port,
        string protocol,
        ResourceExposureScope exposure)
    {
        WithEndpointPort(name, targetPort, port, protocol, exposure);
        return this;
    }

    IProjectResourceBuilder IProjectResourceBuilder.WithHttpEndpoint(
        int? port,
        int targetPort,
        string name)
    {
        WithHttpEndpoint(port, targetPort, name);
        return this;
    }

    IProjectResourceBuilder IProjectResourceBuilder.WithHttpsEndpoint(
        int? port,
        int targetPort,
        string name)
    {
        WithHttpsEndpoint(port, targetPort, name);
        return this;
    }

    IProjectResourceBuilder IProjectResourceBuilder.WithLaunchSettingsEndpoints(bool enabled)
    {
        WithLaunchSettingsEndpoints(enabled);
        return this;
    }

    IProjectResourceBuilder IProjectResourceBuilder.WithHttpHealthCheck(
        string path,
        string? endpointName,
        string name,
        TimeSpan? timeout)
    {
        WithHttpHealthCheck(path, endpointName, name, timeout);
        return this;
    }

    IProjectResourceBuilder IProjectResourceBuilder.WithHttpProbe(
        ResourceProbeType type,
        string path,
        string? endpointName,
        string? name,
        TimeSpan? timeout)
    {
        WithHttpProbe(type, path, endpointName, name, timeout);
        return this;
    }

    IProjectResourceBuilder IProjectResourceBuilder.WithEnvironment(
        IReadOnlyList<EnvironmentVariableAssignment> environmentVariables)
    {
        WithEnvironment(environmentVariables);
        return this;
    }

    IProjectResourceBuilder IProjectResourceBuilder.WithEnvironment(string name, string value)
    {
        WithEnvironment(name, value);
        return this;
    }

    IProjectResourceBuilder IProjectResourceBuilder.WithEnvironment(
        string name,
        ConfigurationEntryReference configurationEntry)
    {
        WithEnvironment(name, configurationEntry);
        return this;
    }

    IProjectResourceBuilder IProjectResourceBuilder.WithEnvironment(
        string name,
        SecretReference secret)
    {
        WithEnvironment(name, secret);
        return this;
    }

    IProjectResourceBuilder IProjectResourceBuilder.WithAppSetting(
        string name,
        string value)
    {
        WithAppSetting(name, value);
        return this;
    }

    IProjectResourceBuilder IProjectResourceBuilder.WithAppSetting(
        string name,
        ConfigurationEntryReference configurationEntry)
    {
        WithAppSetting(name, configurationEntry);
        return this;
    }

    IProjectResourceBuilder IProjectResourceBuilder.WithAppSetting(
        string name,
        SecretReference secret)
    {
        WithAppSetting(name, secret);
        return this;
    }

    IProjectResourceBuilder IProjectResourceBuilder.WithLifetime(ResourceLifetime lifetime)
    {
        declared.Definition = declared.Definition with
        {
            Lifetime = ToApplicationLifetime(lifetime)
        };
        return this;
    }

    IProjectResourceBuilder ILifetimeBoundResourceBuilder<IProjectResourceBuilder>.WithLifetime(
        ResourceLifetime lifetime)
    {
        declared.Definition = declared.Definition with
        {
            Lifetime = ToApplicationLifetime(lifetime)
        };
        return this;
    }

    IProjectResourceBuilder IProjectResourceBuilder.WithServiceDiscovery(bool enabled)
    {
        WithServiceDiscovery(enabled);
        return this;
    }

    IProjectResourceBuilder IProjectResourceBuilder.WithObservability(bool enabled)
    {
        WithObservability(enabled);
        return this;
    }

    IProjectResourceBuilder IProjectResourceBuilder.WithOtlpExporter(
        string? endpoint,
        string? protocol,
        string? headers)
    {
        WithOtlpExporter(endpoint, protocol, headers);
        return this;
    }

    IProjectResourceBuilder IProjectResourceBuilder.WaitFor(IResourceBuilder resource)
    {
        WaitFor(resource);
        return this;
    }

    IProjectResourceBuilder IProjectResourceBuilder.WaitFor(IEnumerable<IResourceBuilder> resources)
    {
        WaitFor(resources);
        return this;
    }

    IProjectResourceBuilder IProjectResourceBuilder.DependsOn(string resourceId)
    {
        DependsOn(resourceId);
        return this;
    }

    IProjectResourceBuilder IProjectResourceBuilder.DependsOn(IResourceBuilder resource)
    {
        DependsOn(resource);
        return this;
    }

    IProjectResourceBuilder IProjectResourceBuilder.DependsOn(IEnumerable<string> resourceIds)
    {
        DependsOn(resourceIds);
        return this;
    }

    IProjectResourceBuilder IProjectResourceBuilder.DependsOn(IEnumerable<IResourceBuilder> resources)
    {
        DependsOn(resources);
        return this;
    }

    IProjectResourceBuilder IProjectResourceBuilder.WithResourceGroup(string? resourceGroupId)
    {
        WithResourceGroup(resourceGroupId);
        return this;
    }

    IProjectResourceBuilder IProjectResourceBuilder.WithParent(string? parentResourceId)
    {
        WithParent(parentResourceId);
        return this;
    }

    IProjectResourceBuilder IProjectResourceBuilder.WithParent(IResourceBuilder resource)
    {
        WithParent(resource);
        return this;
    }

    IProjectResourceBuilder IProjectResourceBuilder.WithReference(IResourceBuilder resource)
    {
        WithReference(resource);
        return this;
    }

    IProjectResourceBuilder IProjectResourceBuilder.WithReferences(IEnumerable<IResourceBuilder> resources)
    {
        WithReferences(resources);
        return this;
    }

    IProjectResourceBuilder IProjectResourceBuilder.Persist(bool overwrite)
    {
        Persist(overwrite);
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
        AddReference(resourceId);

    IResourceBuilder IResourceBuilder.WithReference(IResourceBuilder resource) =>
        WithReference(resource);

    IResourceBuilder IResourceBuilder.WithReferences(IEnumerable<string> resourceIds) =>
        AddReferences(resourceIds);

    private IExecutableResourceBuilder AddReference(string resourceId)
    {
        declared.Definition = declared.Definition with
        {
            References = declared.Definition.References
                .Append(resourceId)
                .ToArray(),
            UseServiceDiscovery = ShouldEnableServiceDiscoveryOnReference()
                ? true
                : declared.Definition.UseServiceDiscovery
        };
        return this;
    }

    private IExecutableResourceBuilder AddReferences(IEnumerable<string> resourceIds)
    {
        ArgumentNullException.ThrowIfNull(resourceIds);
        declared.Definition = declared.Definition with
        {
            References = declared.Definition.References
                .Concat(resourceIds)
                .ToArray(),
            UseServiceDiscovery = ShouldEnableServiceDiscoveryOnReference()
                ? true
                : declared.Definition.UseServiceDiscovery
        };
        return this;
    }

    private bool ShouldEnableServiceDiscoveryOnReference() =>
        string.Equals(
            declared.Definition.ResourceType,
            ApplicationResourceTypes.AspNetCoreProject,
            StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    IResourceBuilder IResourceBuilder.Persist(bool overwrite) =>
        Persist(overwrite);

    IContainerResourceBuilder IContainerResourceBuilder.WithEnvironment(
        IReadOnlyList<EnvironmentVariableAssignment> environmentVariables)
    {
        WithEnvironment(environmentVariables);
        return this;
    }

    IContainerResourceBuilder IContainerResourceBuilder.WithEnvironment(
        string name,
        string value)
    {
        WithEnvironment(name, value);
        return this;
    }

    IContainerResourceBuilder IContainerResourceBuilder.WithEnvironment(
        string name,
        ConfigurationEntryReference configurationEntry)
    {
        WithEnvironment(name, configurationEntry);
        return this;
    }

    IContainerResourceBuilder IContainerResourceBuilder.WithEnvironment(
        string name,
        SecretReference secret)
    {
        WithEnvironment(name, secret);
        return this;
    }

    IContainerResourceBuilder IContainerResourceBuilder.WithAppSetting(
        string name,
        string value)
    {
        WithAppSetting(name, value);
        return this;
    }

    IContainerResourceBuilder IContainerResourceBuilder.WithAppSetting(
        string name,
        ConfigurationEntryReference configurationEntry)
    {
        WithAppSetting(name, configurationEntry);
        return this;
    }

    IContainerResourceBuilder IContainerResourceBuilder.WithAppSetting(
        string name,
        SecretReference secret)
    {
        WithAppSetting(name, secret);
        return this;
    }

    IContainerResourceBuilder IContainerResourceBuilder.WithHttpHealthCheck(
        string path,
        string? endpointName,
        string name,
        TimeSpan? timeout)
    {
        WithHttpHealthCheck(path, endpointName, name, timeout);
        return this;
    }

    IContainerResourceBuilder IContainerResourceBuilder.WithHttpProbe(
        ResourceProbeType type,
        string path,
        string? endpointName,
        string? name,
        TimeSpan? timeout)
    {
        WithHttpProbe(type, path, endpointName, name, timeout);
        return this;
    }

    IContainerResourceBuilder IContainerResourceBuilder.WithVolume(
        string volumeReference,
        string targetPath,
        bool readOnly,
        string? name) =>
        WithVolume(volumeReference, targetPath, readOnly, name);

    IContainerResourceBuilder IContainerResourceBuilder.WithVolume(
        IResourceBuilder volume,
        string targetPath,
        bool readOnly,
        string? name) =>
        WithVolume(volume, targetPath, readOnly, name);

    IContainerResourceBuilder IContainerResourceBuilder.WithReplicas(int replicas)
    {
        WithReplicas(replicas);
        return this;
    }

    IContainerResourceBuilder IContainerResourceBuilder.WithServiceDiscovery(bool enabled)
    {
        WithServiceDiscovery(enabled);
        return this;
    }

    IContainerResourceBuilder IContainerResourceBuilder.WithObservability(bool enabled)
    {
        WithObservability(enabled);
        return this;
    }

    IContainerResourceBuilder IContainerResourceBuilder.WithOtlpExporter(
        string? endpoint,
        string? protocol,
        string? headers)
    {
        WithOtlpExporter(endpoint, protocol, headers);
        return this;
    }

    private ResourceObservability GetCurrentObservability() =>
        declared.Definition.Observability ?? ResourceObservability.Default;

    IContainerResourceBuilder IContainerResourceBuilder.WithResourceGroup(string? resourceGroupId)
    {
        WithResourceGroup(resourceGroupId);
        return this;
    }

    IContainerResourceBuilder IContainerResourceBuilder.WithParent(string? parentResourceId)
    {
        WithParent(parentResourceId);
        return this;
    }

    IContainerResourceBuilder IContainerResourceBuilder.WithParent(IResourceBuilder resource)
    {
        WithParent(resource);
        return this;
    }

    IContainerResourceBuilder IContainerResourceBuilder.DependsOn(string resourceId)
    {
        DependsOn(resourceId);
        return this;
    }

    IContainerResourceBuilder IContainerResourceBuilder.DependsOn(IResourceBuilder resource)
    {
        DependsOn(resource);
        return this;
    }

    IContainerResourceBuilder IContainerResourceBuilder.DependsOn(IEnumerable<string> resourceIds)
    {
        DependsOn(resourceIds);
        return this;
    }

    IContainerResourceBuilder IContainerResourceBuilder.DependsOn(IEnumerable<IResourceBuilder> resources)
    {
        DependsOn(resources);
        return this;
    }

    IContainerResourceBuilder IContainerResourceBuilder.WithReference(string resourceId)
    {
        AddReference(resourceId);
        return this;
    }

    IContainerResourceBuilder IContainerResourceBuilder.WithReference(IResourceBuilder resource)
    {
        WithReference(resource);
        return this;
    }

    IContainerResourceBuilder IContainerResourceBuilder.WithReferences(IEnumerable<string> resourceIds)
    {
        AddReferences(resourceIds);
        return this;
    }

    IContainerResourceBuilder IContainerResourceBuilder.Persist(bool overwrite)
    {
        Persist(overwrite);
        return this;
    }

    private static ApplicationLifetime ToApplicationLifetime(ResourceLifetime lifetime) =>
        lifetime switch
        {
            ResourceLifetime.ControlPlaneScoped => ApplicationLifetime.ControlPlaneScoped,
            _ => ApplicationLifetime.Detached
        };
}
