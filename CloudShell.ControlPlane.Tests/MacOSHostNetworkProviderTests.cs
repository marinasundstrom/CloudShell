using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceManager.Networking;

namespace CloudShell.ControlPlane.Tests;

public sealed class MacOSHostNetworkProviderTests
{
    [Fact]
    public void GetResources_ExcludesMacOSResourceWhenPlatformUnsupported()
    {
        var provider = CreateProvider(isMacOS: false);

        var resources = provider.GetResources();

        Assert.Empty(resources);
    }

    [Fact]
    public async Task ApplyDeclarationAsync_ReportsStableUnsupportedPlatformReason()
    {
        var provider = CreateProvider(isMacOS: false);
        var registrations = new RecordingResourceRegistrationStore();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.ApplyDeclarationAsync(CreateDeclaration(), registrations));

        Assert.Equal(
            "The macOS host networking provider is unavailable because this host is not running macOS.",
            exception.Message);
        Assert.Empty(registrations.GetRegistrations());
    }

    [Fact]
    public async Task ApplyDeclarationAsync_RegistersDeclarationWhenPlatformSupported()
    {
        var provider = CreateProvider(isMacOS: true);
        var registrations = new RecordingResourceRegistrationStore();

        await provider.ApplyDeclarationAsync(CreateDeclaration(), registrations);

        var registration = Assert.Single(registrations.GetRegistrations());
        Assert.Equal(MacOSHostNetworkProvider.ProviderId, registration.ProviderId);
        Assert.Equal(MacOSHostNetworkProvider.ResourceId, registration.ResourceId);
    }

    private static MacOSHostNetworkProvider CreateProvider(bool isMacOS) =>
        new(
            new LocalHostNetworkProvisioner(),
            new HostOperatingSystem(
                isMacOS,
                isWindows: !isMacOS,
                isLinux: false));

    private static ResourceDeclaration CreateDeclaration() =>
        new(
            MacOSHostNetworkProvider.ProviderId,
            MacOSHostNetworkProvider.ResourceId,
            ParentResourceId: null,
            ResourceGroupId: null,
            DateTimeOffset.UtcNow,
            DependsOn: [],
            ResourceDeclarationPersistence.Persisted);

    private sealed class RecordingResourceRegistrationStore : IResourceRegistrationStore
    {
        private readonly Dictionary<string, ResourceRegistration> registrations =
            new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<ResourceRegistration> GetRegistrations() =>
            registrations.Values.ToArray();

        public ResourceRegistration? GetRegistration(string resourceId) =>
            registrations.GetValueOrDefault(resourceId);

        public Task RegisterAsync(
            string providerId,
            string resourceId,
            string? resourceGroupId = null,
            IReadOnlyList<string>? dependsOn = null,
            CancellationToken cancellationToken = default)
        {
            registrations[resourceId] = new ResourceRegistration(
                resourceId,
                providerId,
                resourceGroupId,
                DateTimeOffset.UtcNow,
                dependsOn ?? [],
                Identity: null);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(
            string resourceId,
            CancellationToken cancellationToken = default)
        {
            registrations.Remove(resourceId);
            return Task.CompletedTask;
        }

        public Task AssignToGroupAsync(
            string resourceId,
            string? resourceGroupId,
            IReadOnlyList<string>? dependsOn = null,
            CancellationToken cancellationToken = default)
        {
            var registration = registrations[resourceId];
            registrations[resourceId] = registration with
            {
                ResourceGroupId = resourceGroupId,
                DependsOn = dependsOn ?? registration.DependsOn
            };
            return Task.CompletedTask;
        }

        public Task SetDependenciesAsync(
            string resourceId,
            IReadOnlyList<string> dependsOn,
            CancellationToken cancellationToken = default)
        {
            var registration = registrations[resourceId];
            registrations[resourceId] = registration with { DependsOn = dependsOn };
            return Task.CompletedTask;
        }

        public Task SetIdentityAsync(
            string resourceId,
            ResourceIdentityBinding? identity,
            CancellationToken cancellationToken = default)
        {
            var registration = registrations[resourceId];
            registrations[resourceId] = registration with { Identity = identity };
            return Task.CompletedTask;
        }
    }
}
