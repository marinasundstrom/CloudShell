using CloudShell.Abstractions.Observability;
using CloudShell.ControlPlane.Providers;
using CloudShell.ControlPlane.ResourceModel;
using CloudShell.ResourceModel;
using Microsoft.Extensions.DependencyInjection;
using ResourceManagerState = CloudShell.Abstractions.ResourceManager.ResourceState;
using ResourceModelResource = CloudShell.ResourceModel.Resource;

namespace CloudShell.ControlPlane.Tests;

public sealed class DeviceRegistryResourceManagerProjectionTests
{
    [Fact]
    public async Task BuiltInProjections_ProjectDeviceRegistryEndpointStateAndMonitoring()
    {
        var runtime = new RecordingDeviceRegistryRuntime(ResourceWebAppRuntimeStatus.Running);
        var services = new ServiceCollection();
        services.AddSingleton<IDeviceRegistryRuntimeController>(runtime);
        services.AddSingleton<IDeviceRegistryRuntimeMonitor>(runtime);
        services.AddDeviceRegistryResourceType();
        services.AddBuiltInProviderResourceManagerProjections();

        await using var serviceProvider = services.BuildServiceProvider();
        var resource = ResolveDeviceRegistry(
            "http://localhost:7150",
            "mqtt://localhost:7154");
        var endpointProjection = serviceProvider
            .GetServices<IResourceModelResourceManagerEndpointProjectionProvider>()
            .Select(provider => provider.GetEndpointProjection(resource))
            .SingleOrDefault(projection => projection is { Endpoints.Count: > 0 });
        var state = serviceProvider
            .GetServices<IResourceModelResourceManagerStateProvider>()
            .Select(provider => provider.GetState(resource))
            .SingleOrDefault(projected => projected is not null);
        var managerResource = ResourceModelResourceManagerMapper.ToResourceManagerResource(
            resource,
            new ResourceModelResourceManagerProjectionOptions(
                StateResolver: resource => serviceProvider
                    .GetServices<IResourceModelResourceManagerStateProvider>()
                    .Select(provider => provider.GetState(resource))
                    .FirstOrDefault(projected => projected is not null),
                EndpointProjectionResolver: resource => serviceProvider
                    .GetServices<IResourceModelResourceManagerEndpointProjectionProvider>()
                    .Select(provider => provider.GetEndpointProjection(resource))
                    .FirstOrDefault(projected => projected is not null)));
        var monitoringProvider = serviceProvider
            .GetServices<IResourceMonitoringProvider>()
            .Single(provider => provider.CanMonitor(managerResource));
        var monitoring = await monitoringProvider.GetMonitoringSnapshotAsync(managerResource);

        Assert.NotNull(endpointProjection);
        var projection = endpointProjection;
        Assert.Collection(
            projection.ResourceEndpoints,
            endpoint =>
            {
                Assert.Equal("registry", endpoint.Name);
                Assert.Equal("http", endpoint.Protocol);
                Assert.Equal(7150, endpoint.TargetPort);
            },
            endpoint =>
            {
                Assert.Equal("mqtt", endpoint.Name);
                Assert.Equal("mqtt", endpoint.Protocol);
                Assert.Equal(7154, endpoint.TargetPort);
            });
        Assert.Collection(
            projection.ResourceEndpointNetworkMappings,
            mapping => Assert.Equal(
                "http://localhost:7150/api/device-registries/iot.device-registry%3Adevices",
                mapping.Address),
            mapping => Assert.Equal(
                "mqtt://localhost:7154",
                mapping.Address));
        Assert.Equal(
            "http://localhost:7150/api/device-registries/iot.device-registry%3Adevices",
            projection.ResourceEndpointNetworkMappings[0].Address);
        Assert.Equal(ResourceManagerState.Running, state);
        Assert.Equal(ResourceManagerState.Running, managerResource.State);
        Assert.Equal(
            "http://localhost:7150/api/device-registries/iot.device-registry%3Adevices",
            managerResource.PrimaryEndpoint);
        Assert.NotNull(monitoring);
        Assert.Equal("Device Registry", monitoring.Provider);
        Assert.Equal("Available", monitoring.Status);
        Assert.Contains(monitoring.Metrics, metric => metric.Name == "resource.process.count");
    }

    private static ResourceModelResource ResolveDeviceRegistry(
        string endpoint,
        string? mqttEndpoint = null)
    {
        var attributes = new Dictionary<ResourceAttributeId, ResourceAttributeValue>
        {
            [DeviceRegistryResourceTypeProvider.Attributes.Endpoint] = endpoint
        };
        if (!string.IsNullOrWhiteSpace(mqttEndpoint))
        {
            attributes[DeviceRegistryResourceTypeProvider.Attributes.MqttEndpoint] = mqttEndpoint;
        }

        var state = new CloudShell.ResourceModel.ResourceState(
            "devices",
            DeviceRegistryResourceTypeProvider.ResourceTypeId,
            ResourceId: "iot.device-registry:devices",
            ProviderId: DeviceRegistryResourceTypeProvider.ProviderId,
            Attributes: attributes);
        var typeProvider = new DeviceRegistryResourceTypeProvider();
        var resolver = new ResourceResolver(
            [DeviceRegistryResourceTypeProvider.ClassDefinition],
            [typeProvider.TypeDefinition]);

        return resolver.Resolve(state);
    }

    private sealed class RecordingDeviceRegistryRuntime(
        ResourceWebAppRuntimeStatus status) :
        IDeviceRegistryRuntimeController,
        IDeviceRegistryRuntimeMonitor
    {
        public ResourceWebAppRuntimeStatus GetStatus(ResourceModelResource resource) =>
            status;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteAsync(
            ResourceModelResource resource,
            ResourceOperationId operationId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);

        public ValueTask<ResourceProcessMonitoringSnapshot?> GetMonitoringSnapshotAsync(
            string resourceId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ResourceProcessMonitoringSnapshot?>(new ResourceProcessMonitoringSnapshot(
                ProcessId: 42,
                StartedAt: DateTimeOffset.UtcNow.AddSeconds(-30),
                Timestamp: DateTimeOffset.UtcNow,
                CpuUsagePercent: 1.25,
                TotalProcessorTime: TimeSpan.FromSeconds(3),
                WorkingSetBytes: 1024,
                PrivateMemoryBytes: 512,
                ThreadCount: 4));
    }
}
