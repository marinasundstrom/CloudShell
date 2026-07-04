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
        var resource = ResolveDeviceRegistry("http://localhost:7150");
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
        var endpoint = Assert.Single(projection.ResourceEndpoints);
        Assert.Equal("registry", endpoint.Name);
        Assert.Equal("http", endpoint.Protocol);
        Assert.Equal(7150, endpoint.TargetPort);
        var mapping = Assert.Single(projection.ResourceEndpointNetworkMappings);
        Assert.Equal(
            "http://localhost:7150/api/device-registries/iot.device-registry%3Adevices",
            mapping.Address);
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

    private static ResourceModelResource ResolveDeviceRegistry(string endpoint)
    {
        var state = new CloudShell.ResourceModel.ResourceState(
            "devices",
            DeviceRegistryResourceTypeProvider.ResourceTypeId,
            ResourceId: "iot.device-registry:devices",
            ProviderId: DeviceRegistryResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [DeviceRegistryResourceTypeProvider.Attributes.Endpoint] = endpoint
            });
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
