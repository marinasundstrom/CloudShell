using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.Providers.Applications;
using CloudShell.Providers.Configuration;
using CloudShell.Providers.Docker;
using Microsoft.Extensions.DependencyInjection;

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
        var declarations = store.GetDeclarations();
        var service = Assert.Single(declarations, declaration =>
            declaration.ProviderId == "applications");
        var declaration = Assert.Single(declarations, declaration =>
            declaration.ProviderId == "configuration");

        Assert.Equal("application:configuration-service-configuration-example", service.ResourceId);
        Assert.Equal(ResourceDeclarationPersistence.Transient, service.Persistence);
        Assert.Equal("configuration", declaration.ProviderId);
        Assert.Equal("configuration:example", declaration.ResourceId);
        Assert.Equal("group-1", declaration.ResourceGroupId);
        Assert.Equal([service.ResourceId], declaration.DependsOn);
        Assert.Equal(ResourceDeclarationPersistence.Persisted, declaration.Persistence);
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
    public void TypedDockerContainerBuilder_DeclaresEngineAndContainer()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                var postgres = resources.Declare("managed", "postgres-main");

                resources
                    .AddDocker()
                    .AddContainer("redis", "redis", "7.2")
                    .DependsOn(postgres)
                    .WithResourceGroup("group-1");
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
}
