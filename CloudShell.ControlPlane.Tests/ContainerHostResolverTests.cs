using System.Text.Json;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceManager;

namespace CloudShell.ControlPlane.Tests;

public sealed class ContainerHostResolverTests
{
    [Fact]
    public async Task ResolveAsync_UsesExplicitHostResourceDescriptor()
    {
        var hostResource = CreateResource("docker:remote", "Remote Docker");
        var host = new ContainerHostDescriptor(
            "docker:remote",
            "Remote Docker",
            ContainerHostKind.Docker,
            "tcp://docker.example.test:2376",
            Metadata: new Dictionary<string, string> { ["source"] = "descriptor" });
        var resolver = CreateResolver(
            [hostResource],
            descriptorProviders: [new StaticDescriptorProvider(hostResource.Id, host)]);

        var result = await resolver.ResolveAsync(new ContainerHostResolutionRequest(
            "application:api",
            "team-a",
            ExplicitHostResourceId: hostResource.Id));

        Assert.True(result.IsResolved);
        Assert.Equal("docker:remote", result.Host?.Id);
        Assert.Equal("descriptor", result.Host?.HostMetadata["source"]);
    }

    [Fact]
    public async Task ResolveAsync_UsesConfiguredDefaultHost()
    {
        var resolver = CreateResolver(
            [],
            hostProviders:
            [
                new StaticHostProvider(new ContainerHostDescriptor(
                    "docker:default",
                    "Default Docker",
                    ContainerHostKind.Docker,
                    "unix:///var/run/docker.sock",
                    IsDefault: true))
            ]);

        var result = await resolver.ResolveAsync(new ContainerHostResolutionRequest(
            "application:api",
            null));

        Assert.True(result.IsResolved);
        Assert.Equal("docker:default", result.Host?.Id);
    }

    [Fact]
    public async Task ResolveAsync_UsesCompatibilityEngineProvider()
    {
        var resolver = CreateResolver(
            [],
            engineProviders:
            [
                new StaticEngineProvider(new ContainerEngineResourceDefinition(
                    "docker",
                    "Docker",
                    ContainerEngineKind.Docker,
                    "unix:///var/run/docker.sock",
                    IsDefault: true))
            ]);

        var result = await resolver.ResolveAsync(new ContainerHostResolutionRequest(
            "application:api",
            null));

        Assert.True(result.IsResolved);
        Assert.Equal("docker", result.Host?.Id);
        Assert.Equal(ContainerHostKind.Docker, result.Host?.Kind);
    }

    [Fact]
    public async Task ResolveAsync_PrefersHostProviderOverCompatibilityEngineProvider()
    {
        var resolver = CreateResolver(
            [],
            hostProviders:
            [
                new StaticHostProvider(new ContainerHostDescriptor(
                    "docker",
                    "Docker Host",
                    ContainerHostKind.Docker,
                    "unix:///var/run/docker.sock",
                    IsDefault: true,
                    Metadata: new Dictionary<string, string> { ["source"] = "host" }))
            ],
            engineProviders:
            [
                new StaticEngineProvider(new ContainerEngineResourceDefinition(
                    "docker",
                    "Docker Engine",
                    ContainerEngineKind.Docker,
                    "unix:///var/run/docker.sock",
                    IsDefault: true))
            ]);

        var result = await resolver.ResolveAsync(new ContainerHostResolutionRequest(
            "application:api",
            null));

        Assert.True(result.IsResolved);
        Assert.Equal("Docker Host", result.Host?.Name);
        Assert.Equal("host", result.Host?.HostMetadata["source"]);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsDiagnosticForMissingExplicitHost()
    {
        var resolver = CreateResolver([]);

        var result = await resolver.ResolveAsync(new ContainerHostResolutionRequest(
            "application:api",
            null,
            ExplicitHostResourceId: "docker:missing"));

        Assert.False(result.IsResolved);
        Assert.Equal("Container host 'docker:missing' is not registered.", result.ErrorMessage);
    }

    private static ContainerHostResolver CreateResolver(
        IReadOnlyList<Resource> resources,
        IReadOnlyList<IResourceOrchestrationDescriptorProvider>? descriptorProviders = null,
        IReadOnlyList<IContainerHostProvider>? hostProviders = null,
        IReadOnlyList<IContainerEngineProvider>? engineProviders = null)
    {
        var resourceManager = new TestResourceManagerStore(resources);
        return new ContainerHostResolver(
            resourceManager,
            new TestResourceRegistrationStore(resources),
            descriptorProviders ?? [],
            hostProviders ?? [],
            engineProviders ?? []);
    }

    private static Resource CreateResource(string id, string name) =>
        new(
            id,
            name,
            "docker.host",
            "docker",
            "local",
            ResourceState.Running,
            [],
            "1.0",
            DateTimeOffset.UtcNow,
            [],
            TypeId: "docker.host",
            ResourceClass: ResourceClass.Infrastructure);

    private sealed class StaticHostProvider(ContainerHostDescriptor host) : IContainerHostProvider
    {
        public ContainerHostDescriptor GetDefaultHost() => host;
    }

    private sealed class StaticEngineProvider(ContainerEngineResourceDefinition engine) : IContainerEngineProvider
    {
        public ContainerEngineResourceDefinition GetContainerEngine() => engine;
    }

    private sealed class StaticDescriptorProvider(string resourceId, ContainerHostDescriptor host)
        : IResourceOrchestrationDescriptorProvider
    {
        public bool CanDescribe(Resource resource) =>
            string.Equals(resource.Id, resourceId, StringComparison.OrdinalIgnoreCase);

        public Task<ResourceOrchestrationDescriptor> DescribeAsync(
            Resource resource,
            ResourceOrchestrationDescriptorContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ResourceOrchestrationDescriptor(
                resource.Id,
                ContainerHostResourceTypes.ContainerHost,
                resource.DependsOn,
                [],
                resource.Endpoints,
                "1.0",
                JsonSerializer.SerializeToElement(host)));
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

    private sealed class TestResourceRegistrationStore(IReadOnlyList<Resource> resources) : IResourceRegistrationStore
    {
        public IReadOnlyList<ResourceRegistration> GetRegistrations() =>
            resources
                .Select(resource => new ResourceRegistration(
                    resource.Id,
                    resource.Provider,
                    null,
                    DateTimeOffset.UtcNow,
                    resource.DependsOn))
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
            Task.CompletedTask;

        public Task RemoveAsync(string resourceId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task AssignToGroupAsync(
            string resourceId,
            string? resourceGroupId,
            IReadOnlyList<string>? dependsOn = null,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SetDependenciesAsync(
            string resourceId,
            IReadOnlyList<string> dependsOn,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
