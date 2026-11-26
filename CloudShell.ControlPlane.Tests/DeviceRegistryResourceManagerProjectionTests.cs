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

    [Fact]
    public void BuiltInProjections_ProjectEnrollmentPolicyWithoutUnrelatedCollections()
    {
        var services = new ServiceCollection();
        services.AddBuiltInProviderResourceManagerProjections();
        using var provider = services.BuildServiceProvider();
        var profile = new DeviceEnrollmentProfile
        {
            Name = "factory",
            Policy = new() { SubjectPrefixes = ["device/"] },
            PermissionGrants = [new("configuration.store:settings", "read")]
        };
        var resource = ResolveDeviceRegistry("http://localhost:7150", extraAttributes: new()
        {
            [DeviceRegistryResourceTypeProvider.Attributes.EnrollmentProfiles] = ResourceAttributeValue.FromObject(new[] { profile }),
            [DeviceRegistryResourceTypeProvider.Attributes.AllowedSubjectPrefixes] = ResourceAttributeValue.FromObject(new[] { "device/" }),
            [DeviceRegistryResourceTypeProvider.Attributes.RequiredClaims] = ResourceAttributeValue.FromObject(new[] { new DeviceEnrollmentRequiredClaim("manufacturer", "cloudshell") }),
            [DeviceRegistryResourceTypeProvider.Attributes.TrustedCertificates] = ResourceAttributeValue.FromObject(new[] { new ResourceCertificateReference("secrets.vault:factory", "factory-ca") }),
            ["private.collection"] = ResourceAttributeValue.FromObject(new[] { "not-for-ui" })
        });
        var attributes = provider.GetServices<IResourceModelResourceManagerAttributeProvider>()
            .Select(projection => projection.GetAttributes(resource))
            .Where(values => values is not null)
            .SelectMany(values => values!)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        var projected = ResourceModelResourceManagerMapper.ToResourceManagerResource(resource,
            new ResourceModelResourceManagerProjectionOptions(AttributeResolver: _ => attributes));

        Assert.Equal(4, attributes.Count);
        var profiles = System.Text.Json.JsonSerializer.Deserialize<DeviceEnrollmentProfile[]>(
            projected.ResourceAttributes[DeviceRegistryResourceTypeProvider.Attributes.EnrollmentProfiles.ToString()]);
        Assert.Equal("factory", Assert.Single(profiles!).Name);
        Assert.Equal("device/", Assert.Single(profiles![0].Policy.SubjectPrefixes));
        Assert.Contains("factory-ca", projected.ResourceAttributes[DeviceRegistryResourceTypeProvider.Attributes.TrustedCertificates.ToString()]);
        Assert.Contains("manufacturer", projected.ResourceAttributes[DeviceRegistryResourceTypeProvider.Attributes.RequiredClaims.ToString()]);
        Assert.DoesNotContain("private.collection", projected.ResourceAttributes.Keys);
    }

    private static ResourceModelResource ResolveDeviceRegistry(
        string endpoint,
        string? mqttEndpoint = null,
        Dictionary<ResourceAttributeId, ResourceAttributeValue>? extraAttributes = null)
    {
        var attributes = new Dictionary<ResourceAttributeId, ResourceAttributeValue>
        {
            [DeviceRegistryResourceTypeProvider.Attributes.Endpoint] = endpoint
        };
        if (!string.IsNullOrWhiteSpace(mqttEndpoint))
        {
            attributes[DeviceRegistryResourceTypeProvider.Attributes.MqttEndpoint] = mqttEndpoint;
        }

        foreach (var attribute in extraAttributes ?? [])
        {
            attributes[attribute.Key] = attribute.Value;
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
