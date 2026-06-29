using CloudShell.Abstractions.ResourceManager;
using CloudShell.Providers.Applications;

namespace CloudShell.Abstractions.Tests;

public sealed class ContainerApplicationOrchestratorDeploymentPlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PlanDeployment_EmitsExplicitDeploymentDefinition()
    {
        var planner = CreatePlanner();
        var application = CreateApplication(replicas: 3);

        var plan = planner.PlanDeployment(
            application,
            ResourceState.Running,
            CreateWorkload(replicas: 3),
            revisionHistory: []);

        Assert.Same(plan.Definition, plan.Deployment.Spec.Definition);
        Assert.Equal(plan.Definition, plan.Deployment.Spec.DeploymentDefinition);
        var service = Assert.Single(plan.Definition.DeploymentServices);
        Assert.Equal(ApplicationContainerOrchestratorDeploymentFactory.CreateServiceName(application.Id), service.Name);
        Assert.Equal("registry.local", service.ServiceAttributes[ResourceAttributeNames.ContainerRegistry]);
        Assert.Equal("example/api:v2", service.ServiceAttributes[ResourceAttributeNames.ContainerImage]);
        var replicaGroup = Assert.Single(service.ReplicaGroupDefinitions);
        Assert.Equal("rev-2", replicaGroup.RuntimeRevisionId);
        Assert.Equal(3, replicaGroup.RequestedReplicaSlots);
        Assert.Equal(3, replicaGroup.RequestedReplicas);
    }

    [Fact]
    public void PlanDeployment_UsesRevisionScopedRuntimeInstancesWhenRevisionIsActive()
    {
        var planner = CreatePlanner();
        var application = CreateApplication(replicas: 2);

        var plan = planner.PlanDeployment(
            application,
            ResourceState.Running,
            CreateWorkload(replicas: 2),
            revisionHistory:
            [
                new ApplicationContainerRevisionHistoryEntry(
                    "rev-2",
                    application.Id,
                    "example/api:v2",
                    2,
                    Now,
                    ApplicationContainerRevisionStatuses.Active,
                    ApplicationContainerRevisionChangeKinds.ImageDeployment)
            ]);

        Assert.Equal("rev-2", plan.Deployment.Spec.Service.RuntimeRevisionId);
        var replicaGroup = Assert.Single(Assert.Single(plan.Definition.DeploymentServices).ReplicaGroupDefinitions);
        Assert.Equal("cloudshell-application-api-rev-2-replicas", replicaGroup.Name);
        Assert.Equal("rev-2", replicaGroup.RuntimeRevisionId);
    }

    private static ContainerApplicationOrchestratorDeploymentPlanner CreatePlanner() =>
        new(deployments: new ApplicationContainerOrchestratorDeploymentFactory(
            defaultNetworkName: "cloudshell-test",
            defaultOrchestratorId: "test-orchestrator"));

    private static ApplicationResourceDefinition CreateApplication(int replicas) =>
        new(
            "application:api",
            "API",
            string.Empty,
            containerImage: " example/api:v2 ",
            containerRegistry: " registry.local ",
            replicas: replicas,
            resourceType: ApplicationResourceTypes.ContainerApp,
            containerRevision: " rev-2 ",
            replicasEnabled: replicas > 1);

    private static ResourceWorkloadConfiguration CreateWorkload(int replicas) =>
        new(
            ResourceWorkloadKind.ContainerImage,
            "api",
            Image: "example/api:v2",
            Replicas: replicas,
            ReplicasEnabled: replicas > 1);
}
