using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceManager.Platform;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace CloudShell.ControlPlane.Tests;

public sealed class PlatformResourceProviderTests
{
    [Fact]
    public void GetResources_DoesNotCreateImplicitHostNetworkFromEmptyPlatformStore()
    {
        var provider = new PlatformResourceProvider(
            CreatePlatformStore(),
            new PlatformResourceOptions());

        var resources = provider.GetResources();

        Assert.DoesNotContain(resources, resource =>
            string.Equals(resource.Id, PlatformResourceProvider.HostNetworkResourceId, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task VolumeFilesystemMonitoringProvider_ReportsUsedBytesAndAdvisoryMaxSize()
    {
        var contentRoot = Directory.CreateTempSubdirectory("cloudshell-volume-monitoring-tests-").FullName;
        var environment = new TestHostEnvironment(contentRoot);
        var store = new PlatformResourceStore(
            new PlatformResourceOptions
            {
                DefinitionsPath = "platform-resources.json"
            },
            environment);
        var provider = new PlatformResourceProvider(store, new PlatformResourceOptions(), environment: environment);
        var registrations = new TestResourceRegistrationStore([]);
        var volumePath = Path.Combine(contentRoot, "data");
        Directory.CreateDirectory(volumePath);
        await File.WriteAllTextAsync(Path.Combine(volumePath, "sample.txt"), "1234567890");
        await provider.SetupVolumeAsync(
            new VolumeResourceDefinition(
                "volume:data",
                "Data",
                Location: "data",
                MaxSizeBytes: 20),
            null,
            registrations);
        var volume = Assert.Single(provider.GetResources(), resource => resource.Id == "volume:data");
        var monitoring = new VolumeFilesystemMonitoringProvider(store, environment);

        Assert.True(monitoring.CanMonitor(volume));
        var snapshot = await monitoring.GetMonitoringSnapshotAsync(volume);

        Assert.NotNull(snapshot);
        Assert.Equal("available", snapshot.Status);
        Assert.Equal(10, Assert.Single(snapshot.Metrics, metric => metric.Name == "storage.volume.used").Value);
        Assert.Equal(20, Assert.Single(snapshot.Metrics, metric => metric.Name == "storage.volume.maxSize").Value);
        Assert.Equal(0, Assert.Single(snapshot.Metrics, metric => metric.Name == "storage.volume.maxSizeReached").Value);
    }

    [Fact]
    public async Task VolumeFilesystemMonitoringProvider_WarnsWhenMaxSizeIsReached()
    {
        var contentRoot = Directory.CreateTempSubdirectory("cloudshell-volume-monitoring-tests-").FullName;
        var environment = new TestHostEnvironment(contentRoot);
        var store = new PlatformResourceStore(
            new PlatformResourceOptions
            {
                DefinitionsPath = "platform-resources.json"
            },
            environment);
        var provider = new PlatformResourceProvider(store, new PlatformResourceOptions(), environment: environment);
        var registrations = new TestResourceRegistrationStore([]);
        var volumePath = Path.Combine(contentRoot, "data");
        Directory.CreateDirectory(volumePath);
        await File.WriteAllTextAsync(Path.Combine(volumePath, "sample.txt"), "1234567890");
        await provider.SetupVolumeAsync(
            new VolumeResourceDefinition(
                "volume:data",
                "Data",
                Location: "data",
                MaxSizeBytes: 10),
            null,
            registrations);
        var volume = Assert.Single(provider.GetResources(), resource => resource.Id == "volume:data");
        var monitoring = new VolumeFilesystemMonitoringProvider(store, environment);

        var snapshot = await monitoring.GetMonitoringSnapshotAsync(volume);

        Assert.NotNull(snapshot);
        Assert.Equal("warning", snapshot.Status);
        Assert.Equal(1, Assert.Single(snapshot.Metrics, metric => metric.Name == "storage.volume.maxSizeReached").Value);
        Assert.Contains("at or above max size", snapshot.Message, StringComparison.Ordinal);
    }

    private static PlatformResourceStore CreatePlatformStore()
    {
        var contentRoot = Directory.CreateTempSubdirectory("cloudshell-platform-provider-tests-").FullName;
        return new PlatformResourceStore(
            new PlatformResourceOptions
            {
                DefinitionsPath = "platform-resources.json"
            },
            new TestHostEnvironment(contentRoot));
    }

    private sealed class TestResourceRegistrationStore(IReadOnlyList<ResourceRegistration> registrations) :
        IResourceRegistrationStore
    {
        private readonly Dictionary<string, ResourceRegistration> _registrations = registrations.ToDictionary(
            registration => registration.ResourceId,
            StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<ResourceRegistration> GetRegistrations() => _registrations.Values.ToArray();

        public ResourceRegistration? GetRegistration(string resourceId) =>
            _registrations.GetValueOrDefault(resourceId);

        public Task RegisterAsync(
            string providerId,
            string resourceId,
            string? resourceGroupId = null,
            IReadOnlyList<string>? dependsOn = null,
            CancellationToken cancellationToken = default)
        {
            _registrations[resourceId] = new ResourceRegistration(
                resourceId,
                providerId,
                resourceGroupId,
                DateTimeOffset.UtcNow,
                dependsOn ?? []);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(
            string resourceId,
            CancellationToken cancellationToken = default)
        {
            _registrations.Remove(resourceId);
            return Task.CompletedTask;
        }

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

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "CloudShell.Tests";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(contentRootPath);
    }
}
