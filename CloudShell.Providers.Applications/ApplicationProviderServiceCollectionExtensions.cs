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

    public static IExecutableApplicationResourceBuilder AddExecutable(
        this ICloudShellResourceDeclarationBuilder builder,
        string id,
        string name) =>
        builder.AddExecutableApplication(id, name, executablePath: string.Empty);

    public static IExecutableApplicationResourceBuilder AddExecutableApplication(
        this ICloudShellResourceDeclarationBuilder builder,
        string id,
        string name,
        string executablePath,
        string? arguments = null,
        string? workingDirectory = null,
        string? endpoint = null,
        IReadOnlyList<EnvironmentVariableAssignment>? environmentVariables = null,
        ApplicationLifetime lifetime = ApplicationLifetime.Detached,
        bool useServiceDiscovery = false,
        ResourceObservability? observability = null)
    {
        var definition = new ApplicationResourceDefinition(
            id,
            name,
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
                    DependsOn = declaration.DependsOn
                };
                declared.Persist = declaration.Persistence == ResourceDeclarationPersistence.Persisted;
                declared.OverwritePersistedState = declaration.OverwritePersistedState;
            });

        return new ExecutableApplicationResourceBuilder(resource, declared);
    }

    public static IExecutableApplicationResourceBuilder AddAspNetCoreProjectFromName(
        this ICloudShellResourceDeclarationBuilder builder,
        string name,
        string projectPath,
        string? endpoint = null,
        IReadOnlyList<EnvironmentVariableAssignment>? environmentVariables = null,
        ApplicationLifetime lifetime = ApplicationLifetime.Detached,
        bool hotReload = true,
        bool useServiceDiscovery = false,
        ResourceObservability? observability = null) =>
        builder.AddAspNetCoreProject(
            CreateApplicationResourceId(name),
            name,
            projectPath,
            endpoint,
            environmentVariables,
            lifetime,
            hotReload,
            useServiceDiscovery,
            observability);

    public static IExecutableApplicationResourceBuilder AddAspNetCoreProject(
        this ICloudShellResourceDeclarationBuilder builder,
        string id,
        string name,
        string projectPath,
        string? endpoint = null,
        IReadOnlyList<EnvironmentVariableAssignment>? environmentVariables = null,
        ApplicationLifetime lifetime = ApplicationLifetime.Detached,
        bool hotReload = true,
        bool useServiceDiscovery = false,
        ResourceObservability? observability = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);

        var definition = new ApplicationResourceDefinition(
            id,
            name,
            executablePath: "dotnet",
            arguments: BuildDotNetAspNetCoreProjectArguments(projectPath, hotReload),
            environmentVariables: environmentVariables,
            lifetime: lifetime,
            useServiceDiscovery: useServiceDiscovery,
            endpointPorts: CreateAspNetCoreProjectEndpointPorts(endpoint),
            resourceType: ApplicationResourceTypes.AspNetCoreProject,
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
                    DependsOn = declaration.DependsOn
                };
                declared.Persist = declaration.Persistence == ResourceDeclarationPersistence.Persisted;
                declared.OverwritePersistedState = declaration.OverwritePersistedState;
            });

        return new ExecutableApplicationResourceBuilder(resource, declared);
    }

    public static IContainerResourceBuilder AddContainer(
        this ICloudShellResourceDeclarationBuilder builder,
        string name,
        string image,
        IReadOnlyList<ResourceEndpoint>? endpoints = null,
        IReadOnlyList<EnvironmentVariableAssignment>? environmentVariables = null,
        bool useServiceDiscovery = false,
        int replicas = 1,
        ResourceObservability? observability = null) =>
        builder.AddContainerApplication(
            CreateApplicationResourceId(name),
            name,
            image,
            endpoints,
            environmentVariables,
            useServiceDiscovery,
            replicas,
            observability);

    public static IContainerResourceBuilder AddContainerApplication(
        this ICloudShellResourceDeclarationBuilder builder,
        string id,
        string name,
        string image,
        IReadOnlyList<ResourceEndpoint>? endpoints = null,
        IReadOnlyList<EnvironmentVariableAssignment>? environmentVariables = null,
        bool useServiceDiscovery = false,
        int replicas = 1,
        ResourceObservability? observability = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(image);

        var endpoint = endpoints?.FirstOrDefault(endpoint =>
            !string.IsNullOrWhiteSpace(endpoint.Address))?.Address;
        var definition = new ApplicationResourceDefinition(
            id,
            name,
            executablePath: string.Empty,
            endpoint: endpoint,
            environmentVariables: environmentVariables,
            lifetime: ApplicationLifetime.ControlPlaneScoped,
            useServiceDiscovery: useServiceDiscovery,
            containerImage: image,
            replicas: Math.Max(1, replicas),
            endpointPorts: CreateEndpointPorts(endpoints),
            resourceType: ApplicationResourceTypes.ContainerImage,
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
                    DependsOn = declaration.DependsOn
                };
                declared.Persist = declaration.Persistence == ResourceDeclarationPersistence.Persisted;
                declared.OverwritePersistedState = declaration.OverwritePersistedState;
            });

        return new ExecutableApplicationResourceBuilder(resource, declared);
    }

    private static string CreateApplicationResourceId(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var slug = string.Join(
                "-",
                name.Trim().ToLowerInvariant().Split(
                    [' ', '.', '_', ':', '/', '\\'],
                    StringSplitOptions.RemoveEmptyEntries))
            .Trim('-');
        return string.IsNullOrWhiteSpace(slug)
            ? $"application:{Guid.NewGuid():N}"
            : $"application:{slug}";
    }

    private static string BuildDotNetAspNetCoreProjectArguments(string projectPath, bool hotReload) =>
        hotReload
            ? $"watch --project {QuoteCommandArgument(projectPath)} run --no-launch-profile"
            : $"run --project {QuoteCommandArgument(projectPath)} --no-launch-profile";

    internal static IReadOnlyList<ServicePort> CreateAspNetCoreProjectEndpointPorts(string? endpoint)
    {
        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) &&
            uri.Port > 0)
        {
            return
            [
                new ServicePort(
                    string.IsNullOrWhiteSpace(uri.Scheme) ? "http" : uri.Scheme,
                    uri.Port,
                    uri.Port,
                    string.IsNullOrWhiteSpace(uri.Scheme) ? "http" : uri.Scheme,
                    ResourceExposureScope.Local)
            ];
        }

        return [new ServicePort("http", 80, Protocol: "http", Exposure: ResourceExposureScope.Local)];
    }

    private static string QuoteCommandArgument(string argument)
    {
        if (argument.Length == 0)
        {
            return "\"\"";
        }

        return argument.Any(char.IsWhiteSpace)
            ? $"\"{argument.Replace("\\", "\\\\").Replace("\"", "\\\"")}\""
            : argument;
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
        if (!Uri.TryCreate(endpoint.Address, UriKind.Absolute, out var uri) ||
            uri.Port <= 0)
        {
            return null;
        }

        return new ServicePort(
            string.IsNullOrWhiteSpace(endpoint.Name) ? "default" : endpoint.Name,
            uri.Port,
            uri.Port,
            string.IsNullOrWhiteSpace(endpoint.Protocol) ? uri.Scheme : endpoint.Protocol,
            endpoint.IsExternal ? ResourceExposureScope.Public : ResourceExposureScope.Local);
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

public interface IExecutableApplicationResourceBuilder : ICloudShellResourceBuilder
{
    IExecutableApplicationResourceBuilder WithCommand(
        string executablePath,
        string? arguments = null,
        string? workingDirectory = null);

    IExecutableApplicationResourceBuilder WithEndpoint(string? endpoint);

    IExecutableApplicationResourceBuilder WithEndpointPort(
        string name,
        int targetPort,
        int? port = null,
        string protocol = "http",
        ResourceExposureScope exposure = ResourceExposureScope.Local);

    IExecutableApplicationResourceBuilder WithHttpEndpoint(
        int? port = null,
        int targetPort = 80,
        string name = "http");

    IExecutableApplicationResourceBuilder WithHttpsEndpoint(
        int? port = null,
        int targetPort = 443,
        string name = "https");

    IExecutableApplicationResourceBuilder WithHttpHealthCheck(
        string path,
        string? endpointName = null,
        string name = "health",
        TimeSpan? timeout = null);

    IExecutableApplicationResourceBuilder WithHttpProbe(
        ResourceProbeType type,
        string path,
        string? endpointName = null,
        string? name = null,
        TimeSpan? timeout = null);

    IExecutableApplicationResourceBuilder WithEnvironment(
        IReadOnlyList<EnvironmentVariableAssignment> environmentVariables);

    IExecutableApplicationResourceBuilder WithEnvironment(
        string name,
        string value);

    IExecutableApplicationResourceBuilder WithLifetime(ApplicationLifetime lifetime);

    IExecutableApplicationResourceBuilder WithServiceDiscovery(bool enabled = true);

    IExecutableApplicationResourceBuilder WithObservability(bool enabled = true);

    IExecutableApplicationResourceBuilder WithOtlpExporter(
        string? endpoint = null,
        string? protocol = null,
        string? headers = null);

    IExecutableApplicationResourceBuilder WithContainerImage(string? image);

    IExecutableApplicationResourceBuilder WithDockerBuild(
        string? buildContext,
        string? dockerfile = null);

    IExecutableApplicationResourceBuilder WithReplicas(int replicas);

    IExecutableApplicationResourceBuilder WaitFor(ICloudShellResourceBuilder resource);

    IExecutableApplicationResourceBuilder WaitFor(IEnumerable<ICloudShellResourceBuilder> resources);

    new IExecutableApplicationResourceBuilder DependsOn(string resourceId);

    new IExecutableApplicationResourceBuilder DependsOn(ICloudShellResourceBuilder resource);

    new IExecutableApplicationResourceBuilder DependsOn(IEnumerable<string> resourceIds);

    new IExecutableApplicationResourceBuilder DependsOn(IEnumerable<ICloudShellResourceBuilder> resources);

    new IExecutableApplicationResourceBuilder WithResourceGroup(string? resourceGroupId);

    new IExecutableApplicationResourceBuilder WithParent(string? parentResourceId);

    new IExecutableApplicationResourceBuilder WithParent(ICloudShellResourceBuilder resource);

    new IExecutableApplicationResourceBuilder WithReference(ICloudShellResourceBuilder resource);

    IExecutableApplicationResourceBuilder WithReferences(IEnumerable<ICloudShellResourceBuilder> resources);

    new IExecutableApplicationResourceBuilder Persist(bool overwrite = false);
}

internal sealed class ExecutableApplicationResourceBuilder(
    ICloudShellResourceBuilder inner,
    DeclaredApplicationResource declared) :
    IExecutableApplicationResourceBuilder,
    IContainerResourceBuilder
{
    public ICloudShellBuilder CloudShellBuilder => inner.CloudShellBuilder;

    public string ResourceId => inner.ResourceId;

    public IExecutableApplicationResourceBuilder WithCommand(
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

    public IExecutableApplicationResourceBuilder WithEndpoint(string? endpoint)
    {
        if (string.Equals(
                declared.Definition.ResourceType,
                ApplicationResourceTypes.AspNetCoreProject,
                StringComparison.OrdinalIgnoreCase))
        {
            declared.Definition = declared.Definition with
            {
                Endpoint = null,
                EndpointPorts = ApplicationProviderServiceCollectionExtensions.CreateAspNetCoreProjectEndpointPorts(endpoint)
            };
            return this;
        }

        declared.Definition = declared.Definition with
        {
            Endpoint = endpoint
        };
        return this;
    }

    public IExecutableApplicationResourceBuilder WithEndpointPort(
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
            EndpointPorts = declared.Definition.EndpointPorts
                .Where(endpoint => !string.Equals(endpoint.Name, name, StringComparison.OrdinalIgnoreCase))
                .Append(new ServicePort(name, targetPort, port, protocol, exposure))
                .ToArray()
        };
        return this;
    }

    public IExecutableApplicationResourceBuilder WithHttpEndpoint(
        int? port = null,
        int targetPort = 80,
        string name = "http") =>
        WithEndpointPort(name, targetPort, port, "http");

    public IExecutableApplicationResourceBuilder WithHttpsEndpoint(
        int? port = null,
        int targetPort = 443,
        string name = "https") =>
        WithEndpointPort(name, targetPort, port, "https");

    public IExecutableApplicationResourceBuilder WithHttpHealthCheck(
        string path,
        string? endpointName = null,
        string name = "health",
        TimeSpan? timeout = null) =>
        WithHttpProbe(ResourceProbeType.Health, path, endpointName, name, timeout);

    public IExecutableApplicationResourceBuilder WithHttpProbe(
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

    public IExecutableApplicationResourceBuilder WithEnvironment(
        IReadOnlyList<EnvironmentVariableAssignment> environmentVariables)
    {
        declared.Definition = declared.Definition with
        {
            EnvironmentVariables = environmentVariables
        };
        return this;
    }

    public IExecutableApplicationResourceBuilder WithEnvironment(
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

    public IExecutableApplicationResourceBuilder WithLifetime(ApplicationLifetime lifetime)
    {
        declared.Definition = declared.Definition with { Lifetime = lifetime };
        return this;
    }

    public IExecutableApplicationResourceBuilder WithServiceDiscovery(bool enabled = true)
    {
        declared.Definition = declared.Definition with
        {
            UseServiceDiscovery = enabled
        };
        return this;
    }

    public IExecutableApplicationResourceBuilder WithObservability(bool enabled = true)
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

    public IExecutableApplicationResourceBuilder WithOtlpExporter(
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

    public IExecutableApplicationResourceBuilder WithContainerImage(string? image)
    {
        declared.Definition = declared.Definition with
        {
            ContainerImage = image,
            ContainerBuildContext = null,
            ContainerDockerfile = null
        };
        return this;
    }

    public IContainerResourceBuilder WithImage(string image)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(image);
        WithContainerImage(image);
        return this;
    }

    public IContainerResourceBuilder WithContainerEngine(string containerEngineId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerEngineId);
        declared.Definition = declared.Definition with
        {
            ContainerEngineId = containerEngineId
        };
        return this;
    }

    public IContainerResourceBuilder WithContainerEngine(ICloudShellResourceBuilder containerEngine)
    {
        ArgumentNullException.ThrowIfNull(containerEngine);
        return WithContainerEngine(containerEngine.ResourceId);
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
                .Append(new ServicePort(name, targetPort, port, protocol, exposure))
                .ToArray()
        };
        return this;
    }

    public IExecutableApplicationResourceBuilder WithDockerBuild(
        string? buildContext,
        string? dockerfile = null)
    {
        declared.Definition = declared.Definition with
        {
            ContainerBuildContext = buildContext,
            ContainerDockerfile = dockerfile,
            ContainerImage = null
        };
        return this;
    }

    public IExecutableApplicationResourceBuilder WithReplicas(int replicas)
    {
        declared.Definition = declared.Definition with
        {
            Replicas = Math.Max(1, replicas)
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

    public IExecutableApplicationResourceBuilder WithResourceGroup(string? resourceGroupId)
    {
        inner.WithResourceGroup(resourceGroupId);
        return this;
    }

    public IExecutableApplicationResourceBuilder WithParent(string? parentResourceId)
    {
        inner.WithParent(parentResourceId);
        return this;
    }

    public IExecutableApplicationResourceBuilder WithParent(ICloudShellResourceBuilder resource)
    {
        inner.WithParent(resource);
        return this;
    }

    public IExecutableApplicationResourceBuilder WithReference(ICloudShellResourceBuilder resource)
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

    public IExecutableApplicationResourceBuilder WithReferences(IEnumerable<ICloudShellResourceBuilder> resources)
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

    public IExecutableApplicationResourceBuilder WaitFor(ICloudShellResourceBuilder resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return DependsOn(resource);
    }

    public IExecutableApplicationResourceBuilder WaitFor(IEnumerable<ICloudShellResourceBuilder> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        return DependsOn(resources);
    }

    public IExecutableApplicationResourceBuilder DependsOn(string resourceId)
    {
        inner.DependsOn(resourceId);
        return this;
    }

    public IExecutableApplicationResourceBuilder DependsOn(ICloudShellResourceBuilder resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        inner.DependsOn(resource);
        return this;
    }

    public IExecutableApplicationResourceBuilder DependsOn(IEnumerable<string> resourceIds)
    {
        inner.DependsOn(resourceIds);
        return this;
    }

    public IExecutableApplicationResourceBuilder DependsOn(IEnumerable<ICloudShellResourceBuilder> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        inner.DependsOn(resources.Select(resource =>
        {
            ArgumentNullException.ThrowIfNull(resource);
            return resource.ResourceId;
        }));
        return this;
    }

    public IExecutableApplicationResourceBuilder Persist(bool overwrite = false)
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
        AddReference(resourceId);

    ICloudShellResourceBuilder ICloudShellResourceBuilder.WithReference(ICloudShellResourceBuilder resource) =>
        WithReference(resource);

    ICloudShellResourceBuilder ICloudShellResourceBuilder.WithReferences(IEnumerable<string> resourceIds) =>
        AddReferences(resourceIds);

    private IExecutableApplicationResourceBuilder AddReference(string resourceId)
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

    private IExecutableApplicationResourceBuilder AddReferences(IEnumerable<string> resourceIds)
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

    ICloudShellResourceBuilder ICloudShellResourceBuilder.Persist(bool overwrite) =>
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

    IContainerResourceBuilder IContainerResourceBuilder.WithReplicas(int replicas)
    {
        WithReplicas(replicas);
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

    IContainerResourceBuilder IContainerResourceBuilder.WithParent(ICloudShellResourceBuilder resource)
    {
        WithParent(resource);
        return this;
    }

    IContainerResourceBuilder IContainerResourceBuilder.DependsOn(string resourceId)
    {
        DependsOn(resourceId);
        return this;
    }

    IContainerResourceBuilder IContainerResourceBuilder.DependsOn(ICloudShellResourceBuilder resource)
    {
        DependsOn(resource);
        return this;
    }

    IContainerResourceBuilder IContainerResourceBuilder.DependsOn(IEnumerable<string> resourceIds)
    {
        DependsOn(resourceIds);
        return this;
    }

    IContainerResourceBuilder IContainerResourceBuilder.DependsOn(IEnumerable<ICloudShellResourceBuilder> resources)
    {
        DependsOn(resources);
        return this;
    }

    IContainerResourceBuilder IContainerResourceBuilder.WithReference(string resourceId)
    {
        AddReference(resourceId);
        return this;
    }

    IContainerResourceBuilder IContainerResourceBuilder.WithReference(ICloudShellResourceBuilder resource)
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
