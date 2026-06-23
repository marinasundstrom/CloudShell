using CloudShell.Abstractions.ResourceManager;
using CloudShell.Providers.Applications;

namespace CloudShell.Abstractions.Tests;

public sealed class ApplicationContainerOrchestratorDeploymentFactoryTests
{
    private readonly ApplicationContainerOrchestratorDeploymentFactory factory = new(
        defaultNetworkName: "cloudshell-test",
        defaultOrchestratorId: "test-orchestrator");

    [Fact]
    public void CreateService_MapsResourceWorkloadAndReplicaManagementPolicy()
    {
        var policy = new ResourceOrchestratorReplicaManagementPolicy(
            ResourceOrchestratorReplicaRestartMode.RestartOccupant,
            FailureThreshold: 2);
        var application = CreateApplication() with
        {
            ReplicaManagementPolicy = policy
        };
        var workload = CreateWorkload(replicas: 3);

        var service = factory.CreateService(application, workload);

        Assert.Equal(application.Id, service.ResourceId);
        Assert.Equal(ApplicationContainerOrchestratorDeploymentFactory.CreateServiceName(application.Id), service.Name);
        Assert.Equal(workload, service.Workload);
        Assert.Equal(["cloudshell-test"], service.ServiceNetworks);
        Assert.Equal(policy, service.ReplicaManagementPolicy);
        Assert.Equal(3, service.Replicas);
    }

    [Fact]
    public void CreateDeployment_MapsDeploymentIdentityInputsAndStatus()
    {
        var application = CreateApplication() with
        {
            ContainerImage = " example/api:v2 ",
            ContainerRegistry = " registry.local ",
            ContainerRevision = " rev-2 "
        };
        var workload = CreateWorkload(replicas: 4);

        var deployment = factory.CreateDeployment(
            application,
            ResourceState.Running,
            workload);

        Assert.Equal(ApplicationContainerOrchestratorDeploymentFactory.CreateDeploymentId(application.Id), deployment.Id);
        Assert.Equal("test-orchestrator", deployment.OrchestratorId);
        Assert.Equal(application.Id, deployment.SourceResourceId);
        Assert.Equal(ApplicationContainerOrchestratorDeploymentFactory.CreateServiceName(application.Id), deployment.ServiceId);
        Assert.Equal("rev-2", deployment.RevisionId);
        Assert.Equal(ResourceOrchestratorDeploymentStatus.Active, deployment.Status);
        Assert.Equal("rev-2", deployment.Spec.WorkloadVersion);
        Assert.Null(deployment.Spec.Service.RuntimeRevisionId);
        Assert.Equal("4", deployment.Spec.DeploymentInputs[ResourceAttributeNames.DeploymentRequestedReplicaSlots]);
        Assert.Equal("4", deployment.Spec.DeploymentInputs[ResourceAttributeNames.DeploymentRequestedReplicas]);
        Assert.Equal("registry.local", deployment.Spec.DeploymentInputs[ResourceAttributeNames.ContainerRegistry]);
        Assert.Equal("example/api:v2", deployment.Spec.DeploymentInputs[ResourceAttributeNames.ContainerImage]);
    }

    [Fact]
    public void CreateDeployment_CanScopeRuntimeInstancesToRevision()
    {
        var application = CreateApplication() with
        {
            ContainerRevision = "rev-3"
        };

        var deployment = factory.CreateDeployment(
            application,
            ResourceState.Starting,
            CreateWorkload(replicas: 2),
            useRuntimeRevisionScopedInstances: true);

        Assert.Equal(ResourceOrchestratorDeploymentStatus.Applying, deployment.Status);
        Assert.Equal("rev-3", deployment.Spec.Service.RuntimeRevisionId);
    }

    [Theory]
    [InlineData(ResourceState.Starting, ResourceOrchestratorDeploymentStatus.Applying)]
    [InlineData(ResourceState.Stopping, ResourceOrchestratorDeploymentStatus.Applying)]
    [InlineData(ResourceState.Running, ResourceOrchestratorDeploymentStatus.Active)]
    [InlineData(ResourceState.Degraded, ResourceOrchestratorDeploymentStatus.Failed)]
    [InlineData(ResourceState.Stopped, ResourceOrchestratorDeploymentStatus.Pending)]
    public void GetDeploymentStatus_MapsResourceState(
        ResourceState state,
        ResourceOrchestratorDeploymentStatus expected)
    {
        Assert.Equal(expected, ApplicationContainerOrchestratorDeploymentFactory.GetDeploymentStatus(state));
    }

    private static ApplicationResourceDefinition CreateApplication() =>
        new(
            "application:api",
            "api",
            executablePath: "api",
            resourceType: ApplicationResourceTypes.ContainerApp);

    private static ResourceWorkloadConfiguration CreateWorkload(int replicas) =>
        new(
            ResourceWorkloadKind.ContainerImage,
            "api",
            Image: "example/api:v1",
            Replicas: replicas,
            ReplicasEnabled: replicas > 1);
}
