using CloudShell.Abstractions.ResourceManager;
using CloudShell.Providers.Applications;

namespace CloudShell.Abstractions.Tests;

public sealed class ApplicationResourceProjectionFactoryTests
{
    private static readonly ApplicationResourceProjection ContainerProjection = new(
        _ => true,
        _ => "Container app",
        ApplicationResourceProjectionSupport.GetContainerVersion,
        ApplicationResourceProjectionSupport.GetContainerWorkloadKind,
        _ => ResourceClass.Container);

    private static readonly ApplicationWorkloadConfigurationFactory Workloads = new();
    private static readonly ApplicationContainerOrchestratorDeploymentFactory Deployments = new();

    [Fact]
    public void CreateAttributes_ProjectsReplicaGroupDeploymentAttributes()
    {
        var factory = CreateFactory(new FakeRuntimeStateStore());
        var application = new ApplicationResourceDefinition(
            "application:api",
            "api",
            string.Empty,
            containerImage: "example/api:latest",
            containerHostId: "docker:dev",
            replicas: 3,
            resourceType: ApplicationResourceTypes.ContainerApp,
            replicasEnabled: true,
            replicaManagementPolicy: new ResourceOrchestratorReplicaManagementPolicy(
                ResourceOrchestratorReplicaRestartMode.RestartOccupant,
                FailureThreshold: 2,
                MaxAttempts: 4));

        var attributes = factory.CreateAttributes(
            application,
            ResourceState.Running,
            ContainerProjection);

        Assert.Equal("3", attributes[ResourceAttributeNames.ContainerReplicas]);
        Assert.Equal("true", attributes[ResourceAttributeNames.ContainerReplicasEnabled]);
        Assert.Equal("cloudshell-application-api-deployment", attributes[ResourceAttributeNames.DeploymentId]);
        Assert.Equal("cloudshell-application-api", attributes[ResourceAttributeNames.DeploymentServiceId]);
        Assert.Equal("active", attributes[ResourceAttributeNames.DeploymentStatus]);
        Assert.Equal("3", attributes[ResourceAttributeNames.DeploymentRequestedReplicaSlots]);
        Assert.Equal("3", attributes[ResourceAttributeNames.DeploymentReplicaSlots]);
        Assert.Equal("3", attributes[ResourceAttributeNames.DeploymentReplicaCount]);
        Assert.Equal("3", attributes[ResourceAttributeNames.DeploymentRequestedReplicas]);
        Assert.Equal("3", attributes[ResourceAttributeNames.DeploymentMaterializedReplicas]);
        Assert.Equal("3", attributes[ResourceAttributeNames.DeploymentProjectedReplicas]);
        Assert.Equal(
            ResourceOrchestratorReplicaRestartMode.RestartOccupant.ToString(),
            attributes[ResourceAttributeNames.DeploymentReplicaRestartMode]);
        Assert.Equal("2", attributes[ResourceAttributeNames.DeploymentReplicaFailureThreshold]);
        Assert.Equal("4", attributes[ResourceAttributeNames.DeploymentReplicaMaxAttempts]);
    }

    [Fact]
    public void CreateAttributes_DoesNotProjectMaterializedReplicaSlotsWhenReplicaModeIsDisabled()
    {
        var factory = CreateFactory(new FakeRuntimeStateStore());
        var application = new ApplicationResourceDefinition(
            "application:api",
            "api",
            string.Empty,
            containerImage: "example/api:latest",
            resourceType: ApplicationResourceTypes.ContainerApp);

        var attributes = factory.CreateAttributes(
            application,
            ResourceState.Stopped,
            ContainerProjection);

        Assert.Equal("1", attributes[ResourceAttributeNames.ContainerReplicas]);
        Assert.Equal("false", attributes[ResourceAttributeNames.ContainerReplicasEnabled]);
        Assert.Equal("1", attributes[ResourceAttributeNames.DeploymentRequestedReplicaSlots]);
        Assert.Equal("0", attributes[ResourceAttributeNames.DeploymentReplicaSlots]);
        Assert.Equal("0", attributes[ResourceAttributeNames.DeploymentReplicaCount]);
        Assert.Equal("1", attributes[ResourceAttributeNames.DeploymentRequestedReplicas]);
        Assert.Equal("0", attributes[ResourceAttributeNames.DeploymentMaterializedReplicas]);
        Assert.Equal("0", attributes[ResourceAttributeNames.DeploymentProjectedReplicas]);
    }

    [Theory]
    [InlineData(ResourceState.Running, "unknown")]
    [InlineData(ResourceState.Stopped, "notActive")]
    public void CreateAttributes_ProjectsVolumeMountStatusFromRuntimeState(
        ResourceState state,
        string expectedStatus)
    {
        var factory = CreateFactory(new FakeRuntimeStateStore());
        var application = new ApplicationResourceDefinition(
            "application:api",
            "api",
            string.Empty,
            containerImage: "example/api:latest",
            resourceType: ApplicationResourceTypes.ContainerApp,
            volumeMounts:
            [
                new ResourceVolumeMount("volume:data", "/data")
            ]);

        var attributes = factory.CreateAttributes(
            application,
            state,
            ContainerProjection);

        Assert.Equal("0", attributes[ResourceAttributeNames.VolumeMountMaterializedCount]);
        Assert.Equal(expectedStatus, attributes[ResourceAttributeNames.VolumeMountMaterializationStatus]);
    }

    [Fact]
    public void CreateAttributes_ProjectsMaterializedVolumeMountCount()
    {
        var store = new FakeRuntimeStateStore();
        store.Save(new ApplicationRuntimeState(
            "application:api",
            null,
            null,
            DateTimeOffset.UtcNow,
            VolumeMounts:
            [
                new ResourceVolumeMountMaterialization(
                    "volume:data",
                    "/data",
                    "/tmp/cloudshell/data",
                    ReadOnly: false)
            ]));
        var factory = CreateFactory(store);
        var application = new ApplicationResourceDefinition(
            "application:api",
            "api",
            string.Empty,
            containerImage: "example/api:latest",
            resourceType: ApplicationResourceTypes.ContainerApp,
            volumeMounts:
            [
                new ResourceVolumeMount("volume:data", "/data")
            ]);

        var attributes = factory.CreateAttributes(
            application,
            ResourceState.Running,
            ContainerProjection);

        Assert.Equal("1", attributes[ResourceAttributeNames.VolumeMountMaterializedCount]);
        Assert.Equal("materialized", attributes[ResourceAttributeNames.VolumeMountMaterializationStatus]);
    }

    [Fact]
    public void CreateCapabilities_ProjectsEndpointAndStorageCapabilitiesWhenPresent()
    {
        var application = new ApplicationResourceDefinition(
            "application:api",
            "api",
            string.Empty,
            volumeMounts:
            [
                new ResourceVolumeMount("volume:data", "/data")
            ]);
        var endpoints = new[]
        {
            ResourceEndpoint.Contract("http", "http", ResourceExposureScope.Public, 8080)
        };

        var capabilities = ApplicationResourceProjectionFactory.CreateCapabilities(application, endpoints)
            .Select(capability => capability.Id)
            .ToArray();

        Assert.Contains(ResourceCapabilityIds.EnvironmentVariables, capabilities);
        Assert.Contains(ResourceCapabilityIds.LogSources, capabilities);
        Assert.Contains(ResourceCapabilityIds.Monitoring, capabilities);
        Assert.Contains(ResourceCapabilityIds.EndpointSource, capabilities);
        Assert.Contains(ResourceCapabilityIds.StorageVolumeConsumer, capabilities);
    }

    private static ApplicationResourceProjectionFactory CreateFactory(
        IApplicationRuntimeStateStore store) =>
        new(
            store,
            (application, state, runtimeRevisionScoped) => Deployments.CreateDeployment(
                application,
                state,
                Workloads.Create(application, [], ResourceObservability.None),
                runtimeRevisionScoped));

    private sealed class FakeRuntimeStateStore : IApplicationRuntimeStateStore
    {
        private readonly Dictionary<string, ApplicationRuntimeState> states = new(StringComparer.OrdinalIgnoreCase);

        public ApplicationRuntimeState? Get(string applicationId) =>
            states.GetValueOrDefault(applicationId);

        public void Save(ApplicationRuntimeState state) =>
            states[state.ApplicationId] = state;
    }
}
