using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Providers.Applications;
using CloudShell.Providers.Configuration;
using CloudShell.Providers.Docker;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.Abstractions.Tests;

public sealed class ResourceDeclarationTests
{
    [Fact]
    public void Resources_DeclaresTransientResourceByDefault()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                resources
                    .Declare("configuration", "configuration:example")
                    .WithReference("application:api");
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
                    .WaitFor(postgres)
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
}
