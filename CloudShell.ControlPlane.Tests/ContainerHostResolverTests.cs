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
    public async Task ResolveAsync_UsesPreferredHostResourceDescriptor()
    {
        var hostResource = CreateResource("docker:preferred", "Preferred Docker");
        var host = new ContainerHostDescriptor(
            "docker:preferred",
            "Preferred Docker",
            ContainerHostKind.Docker,
            "tcp://preferred.example.test:2376");
        var resolver = CreateResolver(
            [hostResource],
            descriptorProviders: [new StaticDescriptorProvider(hostResource.Id, host)]);

        var result = await resolver.ResolveAsync(new ContainerHostResolutionRequest(
            "application:api",
            "team-a",
            PreferredHostId: hostResource.Id));

        Assert.True(result.IsResolved);
        Assert.Equal("docker:preferred", result.Host?.Id);
    }

    [Fact]
    public async Task ResolveAsync_UsesHostWhenRequiredCapabilityIsAdvertised()
    {
        var hostResource = CreateResource("docker:remote", "Remote Docker");
        var host = new ContainerHostDescriptor(
            "docker:remote",
            "Remote Docker",
            ContainerHostKind.Docker,
            "tcp://docker.example.test:2376",
            Capabilities: [ContainerHostCapabilityIds.ContainerImage]);
        var resolver = CreateResolver(
            [hostResource],
            descriptorProviders: [new StaticDescriptorProvider(hostResource.Id, host)]);

        var result = await resolver.ResolveAsync(new ContainerHostResolutionRequest(
            "application:api",
            "team-a",
            ExplicitHostResourceId: hostResource.Id,
            RequiredCapability: ContainerHostCapabilityIds.ContainerImage));

        Assert.True(result.IsResolved);
        Assert.Equal("docker:remote", result.Host?.Id);
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
    public async Task ResolveAsync_UsesRegisteredDefaultHostDescriptor()
    {
        var hostResource = CreateResource("docker:registered", "Registered Docker");
        var host = new ContainerHostDescriptor(
            "docker:registered",
            "Registered Docker",
            ContainerHostKind.Docker,
            "tcp://registered.example.test:2376",
            IsDefault: true);
        var resolver = CreateResolver(
            [hostResource],
            descriptorProviders: [new StaticDescriptorProvider(hostResource.Id, host)]);

        var result = await resolver.ResolveAsync(new ContainerHostResolutionRequest(
            "application:api",
            "team-a"));

        Assert.True(result.IsResolved);
        Assert.Equal("docker:registered", result.Host?.Id);
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
        Assert.Equal(ContainerHostResolutionFailureReason.HostNotRegistered, result.FailureReason);
        Assert.Equal("Container host 'docker:missing' is not registered.", result.ErrorMessage);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsDiagnosticForUnavailableExplicitHost()
    {
        var hostResource = CreateResource("docker:remote", "Remote Docker", ResourceState.Stopped);
        var host = new ContainerHostDescriptor(
            "docker:remote",
            "Remote Docker",
            ContainerHostKind.Docker,
            "tcp://docker.example.test:2376");
        var resolver = CreateResolver(
            [hostResource],
            descriptorProviders: [new StaticDescriptorProvider(hostResource.Id, host)]);

        var result = await resolver.ResolveAsync(new ContainerHostResolutionRequest(
            "application:api",
            "team-a",
            ExplicitHostResourceId: hostResource.Id));

        Assert.False(result.IsResolved);
        Assert.Equal(ContainerHostResolutionFailureReason.HostUnavailable, result.FailureReason);
        Assert.Equal("Container host 'docker:remote' is unavailable.", result.ErrorMessage);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsDiagnosticForMissingPreferredHost()
    {
        var resolver = CreateResolver([]);

        var result = await resolver.ResolveAsync(new ContainerHostResolutionRequest(
            "application:api",
            null,
            PreferredHostId: "docker:missing"));

        Assert.False(result.IsResolved);
        Assert.Equal(ContainerHostResolutionFailureReason.HostNotRegistered, result.FailureReason);
        Assert.Equal("Preferred container host 'docker:missing' is not registered.", result.ErrorMessage);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsDiagnosticWhenNoDefaultHostExists()
    {
        var resolver = CreateResolver([]);

        var result = await resolver.ResolveAsync(new ContainerHostResolutionRequest(
            "application:api",
            null));

        Assert.False(result.IsResolved);
        Assert.Equal(ContainerHostResolutionFailureReason.DefaultHostMissing, result.FailureReason);
        Assert.Equal(
            "Resource 'api' is container-backed but no default container host is registered. Use UseDocker(), UseContainerHost(...), or set an explicit container host.",
            result.ErrorMessage);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsDiagnosticForMissingRequiredCapability()
    {
        var hostResource = CreateResource("docker:remote", "Remote Docker");
        var host = new ContainerHostDescriptor(
            "docker:remote",
            "Remote Docker",
            ContainerHostKind.Docker,
            "tcp://docker.example.test:2376",
            Capabilities: [ContainerHostCapabilityIds.ContainerImage]);
        var resolver = CreateResolver(
            [hostResource],
            descriptorProviders: [new StaticDescriptorProvider(hostResource.Id, host)]);

        var result = await resolver.ResolveAsync(new ContainerHostResolutionRequest(
            "application:api",
            "team-a",
            ExplicitHostResourceId: hostResource.Id,
            RequiredCapability: ContainerHostCapabilityIds.ContainerBuild));

        Assert.False(result.IsResolved);
        Assert.Equal(ContainerHostResolutionFailureReason.RequiredCapabilityMissing, result.FailureReason);
        Assert.Equal(
            "Container host 'docker:remote' does not advertise required capability 'container.build'.",
            result.ErrorMessage);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsDiagnosticForUnavailableCredentials()
    {
        var hostResource = CreateResource("docker:remote", "Remote Docker");
        var host = new ContainerHostDescriptor(
            "docker:remote",
            "Remote Docker",
            ContainerHostKind.Docker,
            "tcp://docker.example.test:2376",
            CredentialsAvailable: false,
            Capabilities: [ContainerHostCapabilityIds.ContainerImage]);
        var resolver = CreateResolver(
            [hostResource],
            descriptorProviders: [new StaticDescriptorProvider(hostResource.Id, host)]);

        var result = await resolver.ResolveAsync(new ContainerHostResolutionRequest(
            "application:api",
            "team-a",
            ExplicitHostResourceId: hostResource.Id,
            RequiredCapability: ContainerHostCapabilityIds.ContainerImage));

        Assert.False(result.IsResolved);
        Assert.Equal(ContainerHostResolutionFailureReason.CredentialsUnavailable, result.FailureReason);
        Assert.Equal(
            "Container host 'docker:remote' credentials are unavailable.",
            result.ErrorMessage);
    }

    private static ContainerHostResolver CreateResolver(
        IReadOnlyList<Resource> resources,
        IReadOnlyList<IResourceOrchestrationDescriptorProvider>? descriptorProviders = null,
        IReadOnlyList<IContainerHostProvider>? hostProviders = null)
    {
        var resourceManager = new TestResourceManagerStore(resources);
        return new ContainerHostResolver(
            resourceManager,
            new TestResourceRegistrationStore(resources),
            descriptorProviders ?? [],
            hostProviders ?? []);
    }

    private static Resource CreateResource(
        string id,
        string name,
        ResourceState state = ResourceState.Running) =>
        new(
            id,
            name,
            "docker.host",
            "docker",
            "local",
            state,
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
