using System.Net;
using System.Net.Sockets;
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
    public async Task SetupNetworkAsync_UsesConventionalPortForImplicitEndpoint()
    {
        var conventionalPort = GetFreePort();
        var autoPort = GetFreePort(except: conventionalPort);
        var store = CreatePlatformStore();
        var provider = new PlatformResourceProvider(
            store,
            new PlatformResourceOptions
            {
                AutoLocalPortStart = autoPort,
                AutoLocalPortEnd = autoPort
            });
        var registrations = new TestResourceRegistrationStore([]);

        await provider.SetupNetworkAsync(
            new NetworkResourceDefinition(
                "network:app",
                "App Network",
                Endpoints:
                [
                    new ResourceEndpointRequest(
                        "http",
                        ResourceEndpointProtocol.Http,
                        TargetPort: conventionalPort)
                ]),
            null,
            registrations);

        var network = Assert.Single(provider.GetResources(), resource => resource.Id == "network:app");
        var endpoint = Assert.Single(network.Endpoints);
        Assert.Equal("http", endpoint.Name);
        Assert.Equal(conventionalPort, endpoint.TargetPort);
        var mapping = Assert.Single(network.ResourceEndpointNetworkMappings);
        Assert.Equal($"http://localhost:{conventionalPort}", mapping.Address);
        Assert.Equal("http", mapping.Target.EndpointName);
        Assert.Equal(conventionalPort, Assert.Single(store.GetNetwork("network:app")!.NetworkEndpoints).Port);
    }

    [Fact]
    public async Task SetupNetworkAsync_FallsBackToAutomaticPortWhenConventionalPortIsUnavailable()
    {
        using var listener = ReserveFreePort(out var conventionalPort);
        var autoPort = GetFreePort(except: conventionalPort);
        var store = CreatePlatformStore();
        var provider = new PlatformResourceProvider(
            store,
            new PlatformResourceOptions
            {
                AutoLocalPortStart = autoPort,
                AutoLocalPortEnd = autoPort
            });
        var registrations = new TestResourceRegistrationStore([]);

        await provider.SetupNetworkAsync(
            new NetworkResourceDefinition(
                "network:app",
                "App Network",
                Endpoints:
                [
                    new ResourceEndpointRequest(
                        "http",
                        ResourceEndpointProtocol.Http,
                        TargetPort: conventionalPort,
                        Assignment: ResourceEndpointAssignment.Auto)
                ]),
            null,
            registrations);

        var network = Assert.Single(provider.GetResources(), resource => resource.Id == "network:app");
        var mapping = Assert.Single(network.ResourceEndpointNetworkMappings);
        Assert.Equal($"http://localhost:{autoPort}", mapping.Address);
        Assert.Equal(autoPort, Assert.Single(store.GetNetwork("network:app")!.NetworkEndpoints).Port);
    }

    [Fact]
    public async Task SetupNetworkAsync_FailsProviderDefaultEndpointWhenConventionalPortIsUnavailable()
    {
        using var listener = ReserveFreePort(out var conventionalPort);
        var autoPort = GetFreePort(except: conventionalPort);
        var store = CreatePlatformStore();
        var provider = new PlatformResourceProvider(
            store,
            new PlatformResourceOptions
            {
                AutoLocalPortStart = autoPort,
                AutoLocalPortEnd = autoPort
            });
        var registrations = new TestResourceRegistrationStore([]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.SetupNetworkAsync(
                new NetworkResourceDefinition(
                    "network:sql",
                    "SQL Network",
                    Endpoints:
                    [
                        new ResourceEndpointRequest(
                            "tds",
                            ResourceEndpointProtocol.Tcp,
                            TargetPort: conventionalPort)
                    ]),
                null,
                registrations));

        Assert.Contains("Conventional port", exception.Message, StringComparison.Ordinal);
        Assert.Contains("is not available", exception.Message, StringComparison.Ordinal);
        Assert.Null(store.GetNetwork("network:sql"));
    }

    [Fact]
    public async Task SetupNetworkAsync_AllowsConventionalPortReuseOnDistinctVirtualAddresses()
    {
        const int conventionalPort = 1433;
        var store = CreatePlatformStore();
        var provider = new PlatformResourceProvider(
            store,
            new PlatformResourceOptions());
        var registrations = new TestResourceRegistrationStore([]);

        await provider.SetupNetworkAsync(
            new NetworkResourceDefinition(
                "network:sql-a",
                "SQL A",
                Endpoints:
                [
                    new ResourceEndpointRequest(
                        "tds",
                        ResourceEndpointProtocol.Tcp,
                        TargetPort: conventionalPort,
                        IPAddress: "10.0.0.2")
                ],
                Kind: NetworkResourceKind.Virtual),
            null,
            registrations);
        await provider.SetupNetworkAsync(
            new NetworkResourceDefinition(
                "network:sql-b",
                "SQL B",
                Endpoints:
                [
                    new ResourceEndpointRequest(
                        "tds",
                        ResourceEndpointProtocol.Tcp,
                        TargetPort: conventionalPort,
                        IPAddress: "10.0.0.3")
                ],
                Kind: NetworkResourceKind.Virtual),
            null,
            registrations);

        var resources = provider.GetResources();
        var first = Assert.Single(resources, resource => resource.Id == "network:sql-a");
        var second = Assert.Single(resources, resource => resource.Id == "network:sql-b");
        Assert.Equal("tcp://10.0.0.2:1433", Assert.Single(first.ResourceEndpointNetworkMappings).Address);
        Assert.Equal("tcp://10.0.0.3:1433", Assert.Single(second.ResourceEndpointNetworkMappings).Address);
        Assert.Equal(conventionalPort, Assert.Single(store.GetNetwork("network:sql-a")!.NetworkEndpoints).Port);
        Assert.Equal(conventionalPort, Assert.Single(store.GetNetwork("network:sql-b")!.NetworkEndpoints).Port);
    }

    [Fact]
    public async Task SetupNetworkAsync_AllowsConventionalPortReuseAcrossVirtualNetworks()
    {
        const int conventionalPort = 1433;
        var store = CreatePlatformStore();
        var provider = new PlatformResourceProvider(
            store,
            new PlatformResourceOptions());
        var registrations = new TestResourceRegistrationStore([]);

        await provider.SetupNetworkAsync(
            new NetworkResourceDefinition(
                "network:tenant-a",
                "Tenant A",
                Endpoints:
                [
                    new ResourceEndpointRequest(
                        "tds",
                        ResourceEndpointProtocol.Tcp,
                        TargetPort: conventionalPort,
                        IPAddress: "10.0.0.2")
                ],
                Kind: NetworkResourceKind.Virtual),
            null,
            registrations);
        await provider.SetupNetworkAsync(
            new NetworkResourceDefinition(
                "network:tenant-b",
                "Tenant B",
                Endpoints:
                [
                    new ResourceEndpointRequest(
                        "tds",
                        ResourceEndpointProtocol.Tcp,
                        TargetPort: conventionalPort,
                        IPAddress: "10.0.0.2")
                ],
                Kind: NetworkResourceKind.Virtual),
            null,
            registrations);

        var resources = provider.GetResources();
        var first = Assert.Single(resources, resource => resource.Id == "network:tenant-a");
        var second = Assert.Single(resources, resource => resource.Id == "network:tenant-b");
        Assert.Equal("tcp://10.0.0.2:1433", Assert.Single(first.ResourceEndpointNetworkMappings).Address);
        Assert.Equal("tcp://10.0.0.2:1433", Assert.Single(second.ResourceEndpointNetworkMappings).Address);
    }

    [Fact]
    public async Task SetupNetworkAsync_AllowsMultiplePortsOnSameVirtualAddress()
    {
        var store = CreatePlatformStore();
        var provider = new PlatformResourceProvider(
            store,
            new PlatformResourceOptions());
        var registrations = new TestResourceRegistrationStore([]);

        await provider.SetupNetworkAsync(
            new NetworkResourceDefinition(
                "network:broker",
                "Broker",
                Endpoints:
                [
                    new ResourceEndpointRequest(
                        "amqp",
                        ResourceEndpointProtocol.Tcp,
                        TargetPort: 5672,
                        IPAddress: "10.0.0.4"),
                    new ResourceEndpointRequest(
                        "management",
                        ResourceEndpointProtocol.Http,
                        TargetPort: 15672,
                        IPAddress: "10.0.0.4")
                ],
                Kind: NetworkResourceKind.Virtual),
            null,
            registrations);

        var resource = Assert.Single(provider.GetResources(), resource => resource.Id == "network:broker");
        Assert.Contains(resource.ResourceEndpointNetworkMappings, mapping =>
            mapping.Address == "tcp://10.0.0.4:5672");
        Assert.Contains(resource.ResourceEndpointNetworkMappings, mapping =>
            mapping.Address == "http://10.0.0.4:15672");
    }

    [Fact]
    public async Task ExecuteActionAsync_ReconcileEndpointMappingsPreservesProvisionerErrorSignals()
    {
        var store = CreatePlatformStore();
        var provisioner = new RecordingEndpointMappingProvisioner(
            new ResourceProcedureResult(
                "Provisioner could not start the local proxy.",
                signals:
                [
                    ResourceProcedureSignal.Error("The requested public port is already in use.")
                ]));
        var provider = new PlatformResourceProvider(
            store,
            new PlatformResourceOptions(),
            endpointMappingProvisioners: [provisioner]);
        var registrations = new TestResourceRegistrationStore([]);
        await provider.SetupNetworkAsync(
            new NetworkResourceDefinition(
                "network:host",
                "Host",
                Kind: NetworkResourceKind.Host),
            null,
            registrations);
        await provider.SetupNetworkAsync(
            new NetworkResourceDefinition(
                "network:app",
                "App Network",
                Endpoints:
                [
                    new ResourceEndpointRequest(
                        "api-public",
                        ResourceEndpointProtocol.Http,
                        TargetPort: 5292,
                        Host: "localhost",
                        Port: 5292,
                        Exposure: ResourceExposureScope.Public)
                ],
                EndpointMappings:
                [
                    new ResourceEndpointMappingDefinition(
                        "mapping:api-public",
                        "API public ingress",
                        new ResourceEndpointReference("network:app", "api-public"),
                        new ResourceEndpointReference("application:api", "http"),
                        NetworkResourceId: "network:app",
                        ProviderResourceId: "network:host")
                ],
                Kind: NetworkResourceKind.Virtual),
            null,
            registrations);
        var network = provider.GetResources().Single(resource => resource.Id == "network:app");
        var hostNetwork = provider.GetResources().Single(resource => resource.Id == "network:host");
        var api = CreateResource(
            "application:api",
            "API",
            [ResourceEndpoint.Contract("http", "http", ResourceExposureScope.Local, 5291)],
            endpointNetworkMappings:
            [
                ResourceEndpointNetworkMapping.ForEndpoint(
                    "application:api",
                    "http",
                    "http://localhost:5291")
            ]);
        var resourceManager = new TestResourceManagerStore([network, hostNetwork, api]);

        var result = await provider.ExecuteActionAsync(
            new ResourceProcedureContext(network, null, null, registrations, resourceManager),
            network.ResourceActions.Single(action => action.Id == PlatformResourceProvider.ReconcileEndpointMappingsActionId));

        Assert.Equal("Reconciled 1 endpoint mapping(s), provisioned 0.", result.Message);
        var signal = Assert.Single(result.Signals);
        Assert.Equal(ResourceSignalSeverity.Error, signal.Severity);
        Assert.Equal("The requested public port is already in use.", signal.Message);
        Assert.Single(provisioner.Contexts);
    }

    [Fact]
    public async Task SetupServiceAsync_UsesConventionalPortForImplicitEndpoint()
    {
        var conventionalPort = GetFreePort();
        var autoPort = GetFreePort(except: conventionalPort);
        var store = CreatePlatformStore();
        var provider = new PlatformResourceProvider(
            store,
            new PlatformResourceOptions
            {
                AutoLocalPortStart = autoPort,
                AutoLocalPortEnd = autoPort
            });
        var registrations = new TestResourceRegistrationStore([]);

        await provider.SetupServiceAsync(
            new ServiceResourceDefinition(
                "service:api",
                "API",
                Targets: [new ServiceTarget("application:api")],
                Ports: [new ServicePort("http", conventionalPort)],
                NetworkIds: []),
            null,
            registrations);

        var service = Assert.Single(provider.GetResources(), resource => resource.Id == "service:api");
        var endpoint = Assert.Single(service.Endpoints);
        Assert.Equal("http", endpoint.Name);
        Assert.Equal(conventionalPort, endpoint.TargetPort);
        var mapping = Assert.Single(service.ResourceEndpointNetworkMappings);
        Assert.Equal($"tcp://localhost:{conventionalPort}", mapping.Address);
        Assert.Equal("http", mapping.Target.EndpointName);
        Assert.Equal(conventionalPort, Assert.Single(store.GetService("service:api")!.Ports).Port);
    }

    [Fact]
    public async Task SetupServiceAsync_UsesDefaultNetworkForImplicitEndpointBinding()
    {
        var store = CreatePlatformStore();
        var provider = new PlatformResourceProvider(
            store,
            new PlatformResourceOptions());
        var registrations = new TestResourceRegistrationStore([]);
        await provider.SetupNetworkAsync(
            new NetworkResourceDefinition(
                "network:dev",
                "Development",
                IsDefault: true,
                Kind: NetworkResourceKind.Virtual),
            null,
            registrations);

        await provider.SetupServiceAsync(
            new ServiceResourceDefinition(
                "service:sql",
                "SQL",
                Targets: [new ServiceTarget("application:api")],
                Ports: [new ServicePort("tds", 1433, Protocol: "tcp", IPAddress: "10.0.0.2")],
                NetworkIds: []),
            null,
            registrations);

        var service = Assert.Single(provider.GetResources(), resource => resource.Id == "service:sql");
        var mapping = Assert.Single(service.ResourceEndpointNetworkMappings);
        Assert.Equal("network:dev", mapping.NetworkResourceId);
        Assert.Equal("tcp://10.0.0.2:1433", mapping.Address);
        var port = Assert.Single(store.GetService("service:sql")!.Ports);
        Assert.Equal("network:dev", port.NetworkResourceId);
        Assert.Equal(1433, port.Port);
    }

    [Fact]
    public async Task SetupServiceAsync_RespectsExplicitPortWhenUsingDefaultNetworkBinding()
    {
        var explicitPort = GetFreePort();
        var conventionalPort = GetFreePort(except: explicitPort);
        var store = CreatePlatformStore();
        var provider = new PlatformResourceProvider(
            store,
            new PlatformResourceOptions());
        var registrations = new TestResourceRegistrationStore([]);
        await provider.SetupNetworkAsync(
            new NetworkResourceDefinition(
                "network:dev",
                "Development",
                IsDefault: true,
                Kind: NetworkResourceKind.Virtual),
            null,
            registrations);

        await provider.SetupServiceAsync(
            new ServiceResourceDefinition(
                "service:sql",
                "SQL",
                Targets: [new ServiceTarget("application:api")],
                Ports:
                [
                    new ServicePort(
                        "tds",
                        conventionalPort,
                        explicitPort,
                        "tcp",
                        IPAddress: "10.0.0.2")
                ],
                NetworkIds: []),
            null,
            registrations);

        var service = Assert.Single(provider.GetResources(), resource => resource.Id == "service:sql");
        var mapping = Assert.Single(service.ResourceEndpointNetworkMappings);
        Assert.Equal("network:dev", mapping.NetworkResourceId);
        Assert.Equal($"tcp://10.0.0.2:{explicitPort}", mapping.Address);
        var port = Assert.Single(store.GetService("service:sql")!.Ports);
        Assert.Equal("network:dev", port.NetworkResourceId);
        Assert.Equal(explicitPort, port.Port);
        Assert.Equal(conventionalPort, port.TargetPort);
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

    private static int GetFreePort(int? except = null)
    {
        int port;
        do
        {
            port = GetFreePort();
        }
        while (port == except);

        return port;
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static TcpListener ReserveFreePort(out int port)
    {
        var listener = new TcpListener(IPAddress.Any, 0);
        listener.Start();
        port = ((IPEndPoint)listener.LocalEndpoint).Port;
        return listener;
    }

    private static Resource CreateResource(
        string id,
        string name,
        IReadOnlyList<ResourceEndpoint> endpoints,
        IReadOnlyList<ResourceEndpointNetworkMapping>? endpointNetworkMappings = null) =>
        new(
            id,
            name,
            "Test",
            "Test",
            "test",
            ResourceState.Running,
            endpoints,
            "test",
            DateTimeOffset.UtcNow,
            [],
            EndpointNetworkMappings: endpointNetworkMappings);

    private sealed class RecordingEndpointMappingProvisioner(
        ResourceProcedureResult result) : IResourceEndpointMappingProvisioner
    {
        public List<ResourceEndpointMappingProvisioningContext> Contexts { get; } = [];

        public bool CanProvisionEndpointMapping(
            ResourceEndpointMappingProvisioningContext context) =>
            true;

        public Task<ResourceProcedureResult> ProvisionEndpointMappingAsync(
            ResourceEndpointMappingProvisioningContext context,
            CancellationToken cancellationToken = default)
        {
            Contexts.Add(context);
            return Task.FromResult(result);
        }
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
