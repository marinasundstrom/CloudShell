using CloudShell.Abstractions.ResourceManager;
using CloudShell.Providers.Applications;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.Abstractions.Tests;

public sealed class ApplicationContainerHostResolverTests
{
    [Fact]
    public void ResolveStatic_ReturnsExplicitStaticHost()
    {
        using var serviceProvider = CreateServices(
            CreateHost("docker:default", "Default", isDefault: true),
            CreateHost("docker:selected", "Selected"));
        var resolver = serviceProvider.GetRequiredService<ApplicationContainerHostResolver>();
        var application = new ApplicationResourceDefinition(
            "application:api",
            "api",
            string.Empty,
            containerHostId: "docker:selected");

        var host = resolver.ResolveStatic(application);

        Assert.NotNull(host);
        Assert.Equal("docker:selected", host.Id);
    }

    [Fact]
    public void ResolveStatic_ReturnsDefaultStaticHostWhenNoExplicitHostIsSet()
    {
        using var serviceProvider = CreateServices(
            CreateHost("docker:other", "Other"),
            CreateHost("docker:default", "Default", isDefault: true));
        var resolver = serviceProvider.GetRequiredService<ApplicationContainerHostResolver>();
        var application = new ApplicationResourceDefinition("application:api", "api", string.Empty);

        var host = resolver.ResolveStatic(application);

        Assert.NotNull(host);
        Assert.Equal("docker:default", host.Id);
    }

    [Fact]
    public async Task ResolveAsync_RejectsStaticHostWithoutRequiredCapability()
    {
        using var serviceProvider = CreateServices(
            CreateHost(
                "docker:default",
                "Default",
                isDefault: true,
                capabilities: [ContainerHostCapabilityIds.ContainerImage]));
        var resolver = serviceProvider.GetRequiredService<ApplicationContainerHostResolver>();
        var resourceManager = new TestResourceManagerStore([]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            resolver.ResolveAsync(
                "docker:default",
                null,
                resourceManager,
                ContainerHostCapabilityIds.ContainerBuild,
                CancellationToken.None));

        Assert.Contains(
            "does not advertise required capability 'container.build'",
            exception.Message,
            StringComparison.Ordinal);
    }

    private static ServiceProvider CreateServices(params ContainerHostDescriptor[] hosts)
    {
        var services = new ServiceCollection();
        foreach (var host in hosts)
        {
            services.AddSingleton<IContainerHostProvider>(new StaticContainerHostProvider(host));
        }

        services.AddSingleton<ApplicationContainerHostResolver>();
        return services.BuildServiceProvider();
    }

    private static ContainerHostDescriptor CreateHost(
        string id,
        string name,
        bool isDefault = false,
        IReadOnlyList<string>? capabilities = null) =>
        new(
            id,
            name,
            ContainerHostKind.Docker,
            "unix:///var/run/docker.sock",
            IsDefault: isDefault,
            Capabilities: capabilities);

    private sealed class StaticContainerHostProvider(ContainerHostDescriptor host) : IContainerHostProvider
    {
        public ContainerHostDescriptor GetDefaultHost() => host;
    }

    private sealed class TestResourceManagerStore(IReadOnlyList<Resource> resources) : IResourceManagerStore
    {
        public IReadOnlyList<IResourceProvider> Providers => [];

        public IReadOnlyList<ResourceGroup> GetResourceGroups() => [];

        public IReadOnlyList<Resource> GetAvailableResources() => resources;

        public IReadOnlyList<Resource> GetResources() => resources;

        public IReadOnlyList<ResourceModelDiagnostic> GetResourceModelDiagnostics() => [];

        public ResourceClass? GetResourceTypeClass(string resourceType) => null;

        public Resource? GetResource(string id) =>
            resources.FirstOrDefault(resource => string.Equals(resource.Id, id, StringComparison.OrdinalIgnoreCase));

        public IReadOnlyList<Resource> GetChildren(string resourceId) => [];

        public ResourceGroup? GetGroupForResource(string resourceId) => null;

        public bool IsRegistered(string resourceId) => GetResource(resourceId) is not null;
    }
}
