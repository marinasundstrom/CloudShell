using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.Providers.Applications;
using CloudShell.Providers.Configuration;
using CloudShell.Providers.Docker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using System.Text.Json;

namespace CloudShell.Abstractions.Tests;

public sealed class ResourceDeclarationTests
{
    [Fact]
    public void Resources_DeclaresTransientResourceWithDependencies()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                resources
                    .Declare("configuration", "configuration:example")
                    .DependsOn("application:api");
            });

        var store = services
            .BuildServiceProvider()
            .GetRequiredService<ResourceDeclarationStore>();
        var declaration = Assert.Single(store.GetDeclarations());

        Assert.Equal("configuration", declaration.ProviderId);
        Assert.Equal("configuration:example", declaration.ResourceId);
        Assert.Equal(["application:api"], declaration.DependsOn);
        Assert.Equal(ResourceDeclarationPersistence.Transient, declaration.Persistence);
        Assert.False(declaration.OverwritePersistedState);
    }

    [Fact]
    public void WithReference_RemainsAliasForGenericDependencies()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                var api = resources.Declare("applications", "application:api");

                resources
                    .Declare("configuration", "configuration:example")
                    .WithReference(api);
            });

        var store = services
            .BuildServiceProvider()
            .GetRequiredService<ResourceDeclarationStore>();
        var declaration = Assert.Single(store.GetDeclarations(), declaration =>
            declaration.ResourceId == "configuration:example");

        Assert.Equal(["application:api"], declaration.DependsOn);
    }

    [Fact]
    public void WithParent_RecordsDeclarationParent()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                var parent = resources.Declare("docker", "docker:dev");

                resources
                    .Declare("docker", "docker:container:redis")
                    .WithParent(parent);
            });

        var store = services
            .BuildServiceProvider()
            .GetRequiredService<ResourceDeclarationStore>();
        var declaration = Assert.Single(store.GetDeclarations(), declaration =>
            declaration.ResourceId == "docker:container:redis");

        Assert.Equal("docker:dev", declaration.ParentResourceId);
        Assert.Empty(declaration.DependsOn);
    }

    [Fact]
    public void ResourceManagerStore_AppliesDeclarationParentMetadata()
    {
        var services = new ServiceCollection();
        services
            .AddControlPlane()
            .AddExtension<ParentMetadataExtension>()
            .Resources(resources =>
            {
                var parent = resources.Declare("parent-metadata", "sample:parent");

                resources
                    .Declare("parent-metadata", "sample:child")
                    .WithParent(parent);
            });
        services.AddSingleton<IResourceGroupStore, EmptyResourceGroupStore>();
        services.AddSingleton<IResourceRegistrationStore, DeclarationRegistrationStore>();
        services.AddScoped<IResourceManagerStore, ResourceManagerStore>();

        using var serviceProvider = services.BuildServiceProvider();
        var resources = serviceProvider
            .GetRequiredService<IResourceManagerStore>()
            .GetResources();
        var child = Assert.Single(resources, resource => resource.Id == "sample:child");

        Assert.Equal("sample:parent", child.ParentResourceId);
    }


    [Fact]
    public void Persist_CanRequestOverwrite()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                resources
                    .Declare("configuration", "configuration:example")
                    .Persist(overwrite: true);
            });

        var store = services
            .BuildServiceProvider()
            .GetRequiredService<ResourceDeclarationStore>();
        var declaration = Assert.Single(store.GetDeclarations());

        Assert.Equal(ResourceDeclarationPersistence.Persisted, declaration.Persistence);
        Assert.True(declaration.OverwritePersistedState);
    }

    [Fact]
    public void WithAutoStart_ConfiguresDefaultAndResourceOverrides()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                resources.WithAutoStart(false);

                resources.Declare("configuration", "configuration:inherited");
                resources
                    .Declare("configuration", "configuration:enabled")
                    .WithAutoStart(true);
            });

        var store = services
            .BuildServiceProvider()
            .GetRequiredService<ResourceDeclarationStore>();
        var inherited = Assert.Single(store.GetDeclarations(), declaration =>
            declaration.ResourceId == "configuration:inherited");
        var enabled = Assert.Single(store.GetDeclarations(), declaration =>
            declaration.ResourceId == "configuration:enabled");

        Assert.False(store.DefaultAutoStart);
        Assert.Null(inherited.AutoStartOverride);
        Assert.True(enabled.AutoStartOverride);
        Assert.False(store.ShouldAutoStart("configuration:inherited"));
        Assert.True(store.ShouldAutoStart("configuration:enabled"));
    }

    [Fact]
    public void WithAutoStart_PreservesTypedBuilderFluentChains()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                resources
                    .AddConfigurationStore("configuration:example", "Example Configuration")
                    .WithAutoStart(false)
                    .WithEntry("SampleMessage", "Hello");
            });

        var store = services
            .BuildServiceProvider()
            .GetRequiredService<ResourceDeclarationStore>();
        var declaration = Assert.Single(store.GetDeclarations());

        Assert.False(declaration.AutoStartOverride);
    }

    [Fact]
    public async Task ExecuteAction_DoesNotStartDependencyWhenAutoStartIsDisabled()
    {
        var services = new ServiceCollection();
        services
            .AddControlPlane()
            .Resources(resources =>
            {
                var dependency = resources
                    .Declare("auto-start", "dependency")
                    .WithAutoStart(false);

                resources
                    .Declare("auto-start", "target")
                    .DependsOn(dependency);
            });

        using var serviceProvider = services.BuildServiceProvider();
        var declarations = serviceProvider.GetRequiredService<ResourceDeclarationStore>();
        var provider = new AutoStartResourceProvider();
        var registrations = new DeclarationRegistrationStore(declarations);
        var resourceManager = new TestResourceManagerStore(provider, registrations);
        var selectionStore = new ResourceOrchestratorSelectionStore(
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))),
            new TestOptionsMonitor<ResourceManagerOptions>(new ResourceManagerOptions()));
        var orchestration = new ResourceOrchestrationService(
            [new DefaultResourceOrchestrator()],
            [],
            [],
            resourceManager,
            registrations,
            declarations,
            selectionStore);
        var target = resourceManager.GetResource("target")!;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orchestration.ExecuteActionAsync(
                target,
                ResourceAction.Run,
                startDependencies: true,
                new AllowAllAuthorizationService()));

        Assert.Equal(
            "Dependency resource 'Dependency' is not running and has auto-start disabled.",
            exception.Message);
        Assert.Empty(provider.ExecutedResources);
    }

    [Fact]
    public void TypedConfigurationStoreBuilder_DeclaresResource()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                resources
                    .AddConfigurationStore("configuration:example", "Example Configuration")
                    .WithEntry("SampleMessage", "Hello")
                    .WithResourceGroup("group-1")
                    .Persist();
            });

        var store = services
            .BuildServiceProvider()
            .GetRequiredService<ResourceDeclarationStore>();
        var declaration = Assert.Single(store.GetDeclarations());

        Assert.Equal("configuration", declaration.ProviderId);
        Assert.Equal("configuration:example", declaration.ResourceId);
        Assert.Equal("group-1", declaration.ResourceGroupId);
        Assert.Empty(declaration.DependsOn);
        Assert.Equal(ResourceDeclarationPersistence.Persisted, declaration.Persistence);
    }

    [Fact]
    public async Task ConfigurationProvider_ExposesStoreLogs()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();

        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));
        services
            .AddControlPlane()
            .AddConfigurationProvider(options =>
            {
                options.DefinitionsPath = "configuration-stores.json";
            });

        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<ConfigurationResourceProvider>();
        serviceProvider.GetRequiredService<ConfigurationStore>().Save(
            new ConfigurationStoreDefinition(
                "configuration:example",
                "Example Configuration"));

        var log = Assert.Single(provider.GetLogs());
        var entries = await provider.ReadLogAsync(log.Id);

        Assert.Equal("configuration:example", log.ResourceId);
        Assert.Equal(LogSourceKind.Resource, log.SourceKind);
        Assert.True(log.SupportsStreaming);
        Assert.Empty(entries);
    }

    [Fact]
    public void TypedExecutableBuilder_SeparatesReferencesFromWaitDependencies()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                var settings = resources.AddConfigurationStore(
                    "configuration:settings",
                    "Settings");
                var postgres = resources.Declare("managed", "postgres-main");

                resources
                    .AddExecutableApplication(
                        "application:api",
                        "API",
                        executablePath: "dotnet")
                    .WithReference(settings)
                    .DependsOn(postgres)
                    .WithServiceDiscovery();
            });

        using var serviceProvider = services.BuildServiceProvider();
        var store = serviceProvider.GetRequiredService<ResourceDeclarationStore>();
        var declaration = Assert.Single(
            store.GetDeclarations(),
            declaration => declaration.ResourceId == "application:api");
        var options = serviceProvider.GetRequiredService<ApplicationProviderOptions>();
        var declaredApplications = options
            .GetType()
            .GetProperty("DeclaredApplications", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(options) as System.Collections.IEnumerable;
        var declaredApplication = Assert.Single(declaredApplications!.Cast<object>());
        var application = Assert.IsType<ApplicationResourceDefinition>(
            declaredApplication
                .GetType()
                .GetProperty("Definition")!
                .GetValue(declaredApplication));

        Assert.Equal(["postgres-main"], declaration.DependsOn);
        Assert.Equal(["configuration:settings"], application.References);
        Assert.True(application.UseServiceDiscovery);
    }

    [Fact]
    public void TypedExecutableBuilder_CanDependOnContainerResource()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                var redis = resources
                    .AddDocker()
                    .AddContainer("redis", "redis", "7.2");

                resources
                    .AddExecutableApplication(
                        "application:web",
                        "Web",
                        executablePath: "dotnet")
                    .DependsOn(redis);
            });

        var store = services
            .BuildServiceProvider()
            .GetRequiredService<ResourceDeclarationStore>();
        var declaration = Assert.Single(store.GetDeclarations(), declaration =>
            declaration.ResourceId == "application:web");

        Assert.Equal(["docker:container:redis"], declaration.DependsOn);
    }

    [Fact]
    public void TypedAspNetCoreProjectBuilder_UsesDotNetWatchByDefault()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                var settings = resources.AddConfigurationStore(
                    "configuration:settings",
                    "Settings");

                resources
                    .AddAspNetCoreProject(
                        "application:api",
                        "API",
                        "src/API/API.csproj",
                        endpoint: "http://localhost:5127")
                    .WithReference(settings);
            });

        using var serviceProvider = services.BuildServiceProvider();
        var store = serviceProvider.GetRequiredService<ResourceDeclarationStore>();
        var declaration = Assert.Single(
            store.GetDeclarations(),
            declaration => declaration.ResourceId == "application:api");
        var options = serviceProvider.GetRequiredService<ApplicationProviderOptions>();
        var declaredApplications = options
            .GetType()
            .GetProperty("DeclaredApplications", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(options) as System.Collections.IEnumerable;
        var declaredApplication = Assert.Single(declaredApplications!.Cast<object>());
        var application = Assert.IsType<ApplicationResourceDefinition>(
            declaredApplication
                .GetType()
                .GetProperty("Definition")!
                .GetValue(declaredApplication));

        Assert.Empty(declaration.DependsOn);
        Assert.Equal(ApplicationResourceTypes.AspNetCoreProject, application.ResourceType);
        Assert.Equal("dotnet", application.ExecutablePath);
        Assert.Equal("watch --project src/API/API.csproj run --no-launch-profile", application.Arguments);
        Assert.Null(application.Endpoint);
        Assert.DoesNotContain(
            application.EnvironmentVariables,
            variable => variable.Name == "ASPNETCORE_URLS");
        var port = Assert.Single(application.EndpointPorts);
        Assert.Equal("http", port.Name);
        Assert.Equal(5127, port.TargetPort);
        Assert.Equal(5127, port.Port);
        Assert.Equal("http", port.Protocol);
        Assert.Equal(["configuration:settings"], application.References);
        Assert.True(application.UseServiceDiscovery);
    }

    [Fact]
    public void TypedAspNetCoreProjectBuilder_CanDeclareNamedEndpoints()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                resources
                    .AddAspNetCoreProject(
                        "application:api",
                        "API",
                        "src/API/API.csproj")
                    .WithHttpEndpoint(port: 5127)
                    .WithEndpointPort(
                        "dashboard",
                        targetPort: 18888,
                        port: 18888,
                        protocol: "http");
            });

        using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<ApplicationProviderOptions>();
        var declaredApplications = options
            .GetType()
            .GetProperty("DeclaredApplications", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(options) as System.Collections.IEnumerable;
        var declaredApplication = Assert.Single(declaredApplications!.Cast<object>());
        var application = Assert.IsType<ApplicationResourceDefinition>(
            declaredApplication
                .GetType()
                .GetProperty("Definition")!
                .GetValue(declaredApplication));

        Assert.Collection(
            application.EndpointPorts.OrderBy(port => port.Name, StringComparer.OrdinalIgnoreCase),
            port =>
            {
                Assert.Equal("dashboard", port.Name);
                Assert.Equal(18888, port.TargetPort);
                Assert.Equal(18888, port.Port);
                Assert.Equal("http", port.Protocol);
            },
            port =>
            {
                Assert.Equal("http", port.Name);
                Assert.Equal(80, port.TargetPort);
                Assert.Equal(5127, port.Port);
                Assert.Equal("http", port.Protocol);
            });
    }

    [Fact]
    public void TypedAspNetCoreProjectBuilder_CanDeclareHealthChecks()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                resources
                    .AddAspNetCoreProject(
                        "application:api",
                        "API",
                        "src/API/API.csproj")
                    .WithHttpHealthCheck("/health")
                    .WithHttpProbe(ResourceProbeType.Liveness, "/alive");
            });

        using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<ApplicationProviderOptions>();
        var declaredApplications = options
            .GetType()
            .GetProperty("DeclaredApplications", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(options) as System.Collections.IEnumerable;
        var declaredApplication = Assert.Single(declaredApplications!.Cast<object>());
        var application = Assert.IsType<ApplicationResourceDefinition>(
            declaredApplication
                .GetType()
                .GetProperty("Definition")!
                .GetValue(declaredApplication));

        Assert.Collection(
            application.HealthChecks,
            check =>
            {
                Assert.Equal("/health", check.Path);
                Assert.Equal(ResourceProbeType.Health, check.Type);
                Assert.Equal("health", check.Name);
            },
            check =>
            {
                Assert.Equal("/alive", check.Path);
                Assert.Equal(ResourceProbeType.Liveness, check.Type);
                Assert.Equal("liveness", check.Name);
            });
    }

    [Fact]
    public void TypedAspNetCoreProjectBuilder_AssignsEndpointPortWhenOmitted()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                resources.AddAspNetCoreProject(
                    "application:api",
                    "API",
                    "src/API/API.csproj");
            });

        using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<ApplicationProviderOptions>();
        var declaredApplications = options
            .GetType()
            .GetProperty("DeclaredApplications", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(options) as System.Collections.IEnumerable;
        var declaredApplication = Assert.Single(declaredApplications!.Cast<object>());
        var application = Assert.IsType<ApplicationResourceDefinition>(
            declaredApplication
                .GetType()
                .GetProperty("Definition")!
                .GetValue(declaredApplication));
        var port = Assert.Single(application.EndpointPorts);

        Assert.Equal("http", port.Name);
        Assert.Equal(80, port.TargetPort);
        Assert.Null(port.Port);
        Assert.Equal("http", port.Protocol);
    }

    [Fact]
    public void TypedAspNetCoreProjectBuilder_CanDisableHotReload()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                resources.AddAspNetCoreProject(
                    "application:api",
                    "API",
                    "src/API/API.csproj",
                    hotReload: false);
            });

        using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<ApplicationProviderOptions>();
        var declaredApplications = options
            .GetType()
            .GetProperty("DeclaredApplications", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(options) as System.Collections.IEnumerable;
        var declaredApplication = Assert.Single(declaredApplications!.Cast<object>());
        var application = Assert.IsType<ApplicationResourceDefinition>(
            declaredApplication
                .GetType()
                .GetProperty("Definition")!
                .GetValue(declaredApplication));

        Assert.Equal("run --project src/API/API.csproj --no-launch-profile", application.Arguments);
    }

    [Fact]
    public async Task TypedDockerContainerBuilder_DeclaresEngineAndContainer()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                var postgres = resources.Declare("managed", "postgres-main");

                var container = resources
                    .AddDocker()
                    .AddContainer("redis", "redis", "7.2")
                    .WithLifetime(ResourceLifetime.Detached)
                    .DependsOn(postgres)
                    .WithResourceGroup("group-1");

                Assert.IsAssignableFrom<ILifetimeBoundResourceBuilder<IDockerContainerResourceBuilder>>(container);
            });

        using var serviceProvider = services.BuildServiceProvider();
        var store = serviceProvider.GetRequiredService<ResourceDeclarationStore>();
        var declarations = store.GetDeclarations();
        var engine = Assert.Single(declarations, declaration =>
            declaration.ResourceId == DockerContainerResourceProvider.EngineResourceId);
        var container = Assert.Single(declarations, declaration =>
            declaration.ResourceId == "docker:container:redis");
        var options = serviceProvider.GetRequiredService<DockerProviderOptions>();
        var declaredContainers = options
            .GetType()
            .GetProperty("DeclaredContainers", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(options) as System.Collections.IEnumerable;
        var declaredContainer = Assert.Single(declaredContainers!.Cast<object>());
        var definition = Assert.IsType<DockerContainerResourceDefinition>(
            declaredContainer
                .GetType()
                .GetProperty("Definition")!
                .GetValue(declaredContainer));

        Assert.Equal("docker", engine.ProviderId);
        Assert.Empty(engine.DependsOn);
        Assert.Equal("docker", container.ProviderId);
        Assert.Equal(DockerContainerResourceProvider.EngineResourceId, container.ParentResourceId);
        Assert.Equal("group-1", container.ResourceGroupId);
        Assert.Equal(
            [DockerContainerResourceProvider.EngineResourceId, "postgres-main"],
            container.DependsOn);
        Assert.Equal("redis", definition.Name);
        Assert.Equal("redis:7.2", definition.Image);
        Assert.Equal(DockerContainerResourceProvider.EngineResourceId, definition.DockerResourceId);
        Assert.Equal(container.DependsOn, definition.DependsOn);
        Assert.Equal(ResourceLifetime.Detached, definition.Lifetime);

        using var provider = new DockerContainerResourceProvider(options);
        var resource = Assert.Single(provider.GetResources(), resource =>
            resource.Id == "docker:container:redis");
        var descriptor = await provider.DescribeAsync(
            resource,
            new ResourceOrchestrationDescriptorContext(null, null, null!));
        var workload = descriptor.Configuration.Deserialize<ResourceWorkloadConfiguration>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(ResourceLifetime.Detached, workload?.Lifetime);
    }

    [Fact]
    public void TypedDockerBuilder_ParentsContainersUnderSpecificDockerResource()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                resources
                    .AddDocker("docker:dev", "Development Docker")
                    .AddContainer("redis-dev", "redis", "7.2");

                resources
                    .AddDocker("docker:test", "Test Docker")
                    .AddContainer("redis-test", "redis", "7.2");
            });

        using var serviceProvider = services.BuildServiceProvider();
        var store = serviceProvider.GetRequiredService<ResourceDeclarationStore>();
        var declarations = store.GetDeclarations();
        var devDocker = Assert.Single(declarations, declaration =>
            declaration.ResourceId == "docker:dev");
        var testDocker = Assert.Single(declarations, declaration =>
            declaration.ResourceId == "docker:test");
        var devContainer = Assert.Single(declarations, declaration =>
            declaration.ResourceId == "docker:container:redis-dev");
        var testContainer = Assert.Single(declarations, declaration =>
            declaration.ResourceId == "docker:container:redis-test");
        using var provider = new DockerContainerResourceProvider(
            serviceProvider.GetRequiredService<DockerProviderOptions>());
        var resources = provider.GetResources();
        var devContainerResource = Assert.Single(resources, resource =>
            resource.Id == "docker:container:redis-dev");
        var testContainerResource = Assert.Single(resources, resource =>
            resource.Id == "docker:container:redis-test");

        Assert.Equal("docker", devDocker.ProviderId);
        Assert.Equal("docker", testDocker.ProviderId);
        Assert.Equal("docker:dev", devContainer.ParentResourceId);
        Assert.Equal("docker:test", testContainer.ParentResourceId);
        Assert.Equal(["docker:dev"], devContainer.DependsOn);
        Assert.Equal(["docker:test"], testContainer.DependsOn);
        Assert.Equal("docker:dev", devContainerResource.ParentResourceId);
        Assert.Equal("docker:test", testContainerResource.ParentResourceId);
    }

    [Fact]
    public async Task DockerProvider_DescribesEngineAsGenericContainerEngine()
    {
        using var provider = new DockerContainerResourceProvider(new DockerProviderOptions());
        var engine = Assert.Single(provider.GetResources(), resource =>
            resource.Id == DockerContainerResourceProvider.EngineResourceId);

        Assert.True(provider.CanDescribe(engine));

        var descriptor = await provider.DescribeAsync(
            engine,
            new ResourceOrchestrationDescriptorContext(null, null, null!));
        var definition = descriptor.Configuration.Deserialize<ContainerEngineResourceDefinition>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(ContainerEngineResourceTypes.ContainerEngine, descriptor.ResourceType);
        Assert.NotNull(definition);
        Assert.Equal(ContainerEngineKind.Docker, definition.Kind);
        Assert.True(definition.IsDefault);
        Assert.Equal(provider.Endpoint.ToString(), definition.Endpoint);
    }

    [Fact]
    public void UseDocker_RegistersImplicitContainerEngineWithoutResourceDeclaration()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .UseDocker();

        using var serviceProvider = services.BuildServiceProvider();
        var declarations = serviceProvider
            .GetRequiredService<ResourceDeclarationStore>()
            .GetDeclarations();
        var engine = Assert.Single(serviceProvider.GetServices<IContainerEngineProvider>())
            .GetContainerEngine();

        Assert.Empty(declarations);
        Assert.Equal("docker", engine.Id);
        Assert.Equal(ContainerEngineKind.Docker, engine.Kind);
        Assert.True(engine.IsDefault);
    }

    [Fact]
    public void TypedDockerContainerBuilder_NormalizesExplicitContainerId()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                resources
                    .AddDockerContainer("rabbitmq", "RabbitMQ", "rabbitmq:4")
                    .DependsOn("configuration:settings")
                    .DependsOn(DockerContainerResourceProvider.EngineResourceId);
            });

        var store = services
            .BuildServiceProvider()
            .GetRequiredService<ResourceDeclarationStore>();
        var container = Assert.Single(store.GetDeclarations(), declaration =>
            declaration.ResourceId == "docker:container:rabbitmq");

        Assert.Equal(
            [DockerContainerResourceProvider.EngineResourceId, "configuration:settings"],
            container.DependsOn);
    }

    [Fact]
    public void PlatformResources_DeclareNetworkAndServiceWithExposure()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                var network = resources.AddNetwork("network:app", "App Network");
                var api = resources.Declare("applications", "application:api");

                resources
                    .AddService("service:api", "API")
                    .Targets(api)
                    .WithNetwork(network)
                    .WithPort(
                        "http",
                        targetPort: 8080,
                        port: 5080,
                        protocol: "http",
                        exposure: ResourceExposureScope.Public);
            });

        using var serviceProvider = services.BuildServiceProvider();
        var store = serviceProvider.GetRequiredService<ResourceDeclarationStore>();
        var service = Assert.Single(store.GetDeclarations(), declaration =>
            declaration.ResourceId == "service:api");

        Assert.Equal(PlatformResourceProvider.ProviderId, service.ProviderId);
        Assert.Equal(["application:api", "network:app"], service.DependsOn);

        var options = serviceProvider.GetRequiredService<PlatformResourceOptions>();
        var serviceDefinition = Assert.Single(options.DeclaredServices).Definition;

        Assert.Equal("service:api", serviceDefinition.Id);
        Assert.Equal([new ServiceTarget("application:api")], serviceDefinition.Targets);
        Assert.Equal(["network:app"], serviceDefinition.NetworkIds);

        var port = Assert.Single(serviceDefinition.Ports);
        Assert.Equal("http", port.Name);
        Assert.Equal(8080, port.TargetPort);
        Assert.Equal(5080, port.Port);
        Assert.Equal(ResourceExposureScope.Public, port.Exposure);
    }

    [Fact]
    public void ExecutableApplicationBuilder_CanAttachContainerImageForOrchestration()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                resources
                    .AddExecutableApplication(
                        "application:api",
                        "API",
                        executablePath: "dotnet",
                        arguments: "run")
                    .WithContainerImage("example/api:dev")
                    .WithReplicas(2);
            });

        using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<ApplicationProviderOptions>();
        var declaredApplications = options
            .GetType()
            .GetProperty("DeclaredApplications", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(options) as System.Collections.IEnumerable;
        var declaredApplication = Assert.Single(declaredApplications!.Cast<object>());
        var application = Assert.IsType<ApplicationResourceDefinition>(
            declaredApplication
                .GetType()
                .GetProperty("Definition")!
                .GetValue(declaredApplication));

        Assert.Equal("example/api:dev", application.ContainerImage);
        Assert.Null(application.ContainerBuildContext);
        Assert.Equal(2, application.Replicas);
    }

    [Fact]
    public async Task ContainerApplicationBuilder_DeclaresTopLevelContainerWorkload()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IHostEnvironment>(
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        services
            .AddControlPlane()
            .AddExtension<ApplicationProviderExtension>()
            .Resources(resources =>
            {
                var container = resources
                    .AddContainer(
                        "sql",
                        "mcr.microsoft.com/mssql/server:2022-latest",
                        replicas: 1)
                    .WithImage("example/sql-server:dev")
                    .WithEndpoint("tds", targetPort: 1433, port: 14333)
                    .WithContainerEngine("docker:dev")
                    .WithLifetime(ResourceLifetime.Detached);

                Assert.IsAssignableFrom<ILifetimeBoundResourceBuilder<IContainerResourceBuilder>>(container);
            });

        using var serviceProvider = services.BuildServiceProvider();
        var store = serviceProvider.GetRequiredService<ResourceDeclarationStore>();
        var declaration = Assert.Single(store.GetDeclarations(), declaration =>
            declaration.ResourceId == "application:sql");
        var provider = ActivatorUtilities.CreateInstance<ApplicationResourceProvider>(serviceProvider);
        var resource = Assert.Single(provider.GetResources(), resource =>
            resource.Id == "application:sql");
        var descriptor = await provider.DescribeAsync(
            resource,
            new ResourceOrchestrationDescriptorContext(null, null, null!));
        var workload = descriptor.Configuration.Deserialize<ResourceWorkloadConfiguration>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal("applications", declaration.ProviderId);
        Assert.Null(declaration.ParentResourceId);
        Assert.Empty(declaration.DependsOn);
        Assert.Equal(ApplicationResourceTypes.ContainerImage, resource.EffectiveTypeId);
        Assert.Equal(ApplicationLifetime.Detached, provider.GetApplication("application:sql")?.Lifetime);
        Assert.Equal(ResourceWorkloadKind.ContainerImage, workload?.Kind);
        Assert.Equal("example/sql-server:dev", workload?.Image);
        Assert.Equal("docker:dev", workload?.ContainerEngineId);
        Assert.Equal(ResourceLifetime.Detached, workload?.Lifetime);
        var port = Assert.Single(workload?.WorkloadPorts ?? []);
        Assert.Equal("tds", port.Name);
        Assert.Equal(1433, port.TargetPort);
        Assert.Equal(14333, port.Port);
        var endpoint = Assert.Single(resource.Endpoints);
        Assert.Equal("tcp://localhost:14333", endpoint.Address);
    }

    private sealed class ParentMetadataExtension : ICloudShellExtension
    {
        public CloudShellExtensionManifest Manifest => new(
            "sample.parent-metadata",
            "Parent Metadata",
            "Adds resources for parent metadata tests.",
            "0.1.0",
            [],
            []);

        public void Configure(ICloudShellExtensionBuilder builder)
        {
            builder.AddResourceProvider<ParentMetadataProvider>();
        }
    }

    private sealed class ParentMetadataProvider : IResourceProvider
    {
        public string Id => "parent-metadata";

        public string DisplayName => "Parent Metadata";

        public IReadOnlyList<CloudResource> GetResources() =>
        [
            new(
                "sample:parent",
                "Parent",
                "Sample Parent",
                DisplayName,
                "local",
                ResourceState.Running,
                [],
                "1.0",
                DateTimeOffset.UtcNow,
                []),
            new(
                "sample:child",
                "Child",
                "Sample Child",
                DisplayName,
                "local",
                ResourceState.Running,
                [],
                "1.0",
                DateTimeOffset.UtcNow,
                [])
        ];
    }

    private sealed class DeclarationRegistrationStore(
        ResourceDeclarationStore declarations) : IResourceRegistrationStore
    {
        public IReadOnlyList<ResourceRegistration> GetRegistrations() =>
            declarations.GetDeclarations()
                .Select(declaration => new ResourceRegistration(
                    declaration.ResourceId,
                    declaration.ProviderId,
                    declaration.ResourceGroupId,
                    declaration.DeclaredAt,
                    declaration.DependsOn))
                .ToArray();

        public ResourceRegistration? GetRegistration(string resourceId) =>
            GetRegistrations().FirstOrDefault(registration =>
                string.Equals(registration.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase));

        public Task RegisterAsync(
            string providerId,
            string resourceId,
            string? resourceGroupId = null,
            IReadOnlyList<string>? dependsOn = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RemoveAsync(
            string resourceId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AssignToGroupAsync(
            string resourceId,
            string? resourceGroupId,
            IReadOnlyList<string>? dependsOn = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SetDependenciesAsync(
            string resourceId,
            IReadOnlyList<string> dependsOn,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class AutoStartResourceProvider :
        IResourceProvider,
        IResourceProcedureProvider
    {
        private readonly Dictionary<string, ResourceState> states = new(StringComparer.OrdinalIgnoreCase)
        {
            ["dependency"] = ResourceState.Stopped,
            ["target"] = ResourceState.Stopped
        };

        public string Id => "auto-start";

        public string DisplayName => "Auto Start";

        public List<string> ExecutedResources { get; } = [];

        public IReadOnlyList<CloudResource> GetResources() =>
            states
                .Select(item => new CloudResource(
                    item.Key,
                    item.Key == "dependency" ? "Dependency" : "Target",
                    "Sample",
                    DisplayName,
                    "local",
                    item.Value,
                    [],
                    "1.0",
                    DateTimeOffset.UtcNow,
                    [],
                    Actions: [ResourceAction.Run]))
                .ToArray();

        public Task<ResourceProcedureResult> DeleteAsync(
            ResourceProcedureContext context,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ResourceProcedureResult> ExecuteActionAsync(
            ResourceProcedureContext context,
            ResourceAction action,
            CancellationToken cancellationToken = default)
        {
            ExecutedResources.Add(context.Resource.Id);
            states[context.Resource.Id] = ResourceState.Running;
            return Task.FromResult(ResourceProcedureResult.Completed("Completed."));
        }
    }

    private sealed class TestResourceManagerStore(
        IResourceProvider provider,
        IResourceRegistrationStore registrations) : IResourceManagerStore
    {
        public IReadOnlyList<IResourceProvider> Providers => [provider];

        public IReadOnlyList<ResourceGroup> GetResourceGroups() => [];

        public IReadOnlyList<CloudResource> GetAvailableResources() =>
            provider.GetResources();

        public IReadOnlyList<CloudResource> GetResources() =>
            GetAvailableResources()
                .Select(resource =>
                {
                    var registration = registrations.GetRegistration(resource.Id);
                    return registration is null
                        ? resource
                        : resource with
                        {
                            DependsOn = resource.DependsOn
                                .Concat(registration.DependsOn)
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .ToArray()
                        };
                })
                .ToArray();

        public CloudResource? GetResource(string id) =>
            GetResources().FirstOrDefault(resource =>
                string.Equals(resource.Id, id, StringComparison.OrdinalIgnoreCase));

        public IReadOnlyList<CloudResource> GetChildren(string resourceId) => [];

        public ResourceGroup? GetGroupForResource(string resourceId) => null;

        public bool IsRegistered(string resourceId) =>
            registrations.GetRegistration(resourceId) is not null;
    }

    private sealed class AllowAllAuthorizationService : ICloudShellAuthorizationService
    {
        public bool IsAuthenticated => true;

        public bool HasPermission(string permission) => true;

        public bool CanAccessResourceGroup(string? resourceGroupId, string permission) => true;

        public bool CanAccessResource(string resourceId, string? resourceGroupId, string permission) => true;
    }

    private sealed class TestOptionsMonitor<TOptions>(TOptions currentValue) :
        Microsoft.Extensions.Options.IOptionsMonitor<TOptions>
    {
        public TOptions CurrentValue { get; } = currentValue;

        public TOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<TOptions, string?> listener) => null;
    }

    private sealed class EmptyResourceGroupStore : IResourceGroupStore
    {
        public IReadOnlyList<ResourceGroup> GetResourceGroups() => [];

        public ResourceGroup? GetGroupForResource(string resourceId) => null;

        public Task<ResourceGroup> CreateAsync(
            string name,
            string description,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "CloudShell.Tests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
