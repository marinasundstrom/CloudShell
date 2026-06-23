using CloudShell.Abstractions.ResourceManager;
using CloudShell.Providers.Applications;

namespace CloudShell.Abstractions.Tests;

public sealed class ContainerApplicationDeploymentTearDownPlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 23, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PlanTearDown_RetiresSupersededRevisionReplicaGroup()
    {
        var planner = new ContainerApplicationDeploymentTearDownPlanner();
        var application = CreateApplication();
        var defaultService = CreateService(replicas: 2);
        var appDeployment = new ApplicationContainerDeployment(
            "dep-2",
            application.Id,
            "rev-2",
            "rev-1",
            "example/api:next",
            2,
            Now,
            ApplicationContainerDeploymentStatuses.Completed,
            ApplicationContainerRevisionChangeKinds.ImageDeployment,
            OrchestratorDeploymentId: "orchestrator-dep");
        var basedOnRevision = new ApplicationContainerRevisionHistoryEntry(
            "rev-1",
            application.Id,
            "example/api:latest",
            3,
            Now.AddMinutes(-10),
            ApplicationContainerRevisionStatuses.Superseded,
            ApplicationContainerRevisionChangeKinds.Initial,
            DeploymentId: "dep-1",
            RevisionNumber: 1);

        var requests = planner.PlanTearDown(
            application,
            CreateApplyResult("orchestrator-dep", "rev-2", replicaGroup: null),
            appDeployment,
            basedOnRevision,
            defaultService,
            _ => true);

        var request = Assert.Single(requests);

        Assert.Equal("rev-1", request.Service.RuntimeRevisionId);
        Assert.Equal(3, request.Service.Replicas);
        Assert.True(request.Service.ReplicasEnabled);
        Assert.NotNull(request.ReplicaGroup);
        Assert.Equal("cloudshell-application-api-rev-1-replicas", request.ReplicaGroup.Id);
        Assert.Contains("rev-1", request.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PlanTearDown_RetiresLegacyStableReplicaGroupWhenVisible()
    {
        var planner = new ContainerApplicationDeploymentTearDownPlanner();
        var application = CreateApplication();
        var defaultService = CreateService(replicas: 2);
        var appliedService = defaultService with { RuntimeRevisionId = "rev-2" };
        var appliedReplicaGroup = ResourceOrchestratorReplicaGroups.CreateDefaultReplicaGroup(appliedService);

        var requests = planner.PlanTearDown(
            application,
            CreateApplyResult("orchestrator-dep", "rev-2", appliedReplicaGroup),
            appDeployment: null,
            basedOnRevision: null,
            defaultService: defaultService,
            hasVisibleLegacyReplicaGroup: _ => true);

        var request = Assert.Single(requests);

        Assert.Null(request.Service.RuntimeRevisionId);
        Assert.Equal(2, request.Service.Replicas);
        Assert.NotNull(request.ReplicaGroup);
        Assert.Equal("cloudshell-application-api-replicas", request.ReplicaGroup.Id);
        Assert.Contains("legacy stable", request.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PlanTearDown_SkipsLegacyStableReplicaGroupWhenNotVisible()
    {
        var planner = new ContainerApplicationDeploymentTearDownPlanner();
        var defaultService = CreateService(replicas: 2);
        var appliedService = defaultService with { RuntimeRevisionId = "rev-2" };
        var appliedReplicaGroup = ResourceOrchestratorReplicaGroups.CreateDefaultReplicaGroup(appliedService);

        var requests = planner.PlanTearDown(
            CreateApplication(),
            CreateApplyResult("orchestrator-dep", "rev-2", appliedReplicaGroup),
            appDeployment: null,
            basedOnRevision: null,
            defaultService: defaultService,
            hasVisibleLegacyReplicaGroup: _ => false);

        Assert.Empty(requests);
    }

    private static ApplicationResourceDefinition CreateApplication() =>
        new(
            "application:api",
            "API",
            string.Empty,
            containerImage: "example/api:next",
            replicas: 2,
            resourceType: ApplicationResourceTypes.ContainerApp,
            containerRevision: "rev-2",
            replicasEnabled: true,
            containerRevisions:
            [
                new ApplicationContainerRevision(
                    "rev-1",
                    "example/api:latest",
                    3,
                    Now.AddMinutes(-10),
                    ApplicationContainerRevisionChangeKinds.Initial,
                    RevisionNumber: 1),
                new ApplicationContainerRevision(
                    "rev-2",
                    "example/api:next",
                    2,
                    Now,
                    ApplicationContainerRevisionChangeKinds.ImageDeployment,
                    BasedOnRevisionId: "rev-1",
                    RevisionNumber: 2)
            ]);

    private static ResourceOrchestratorService CreateService(int replicas) =>
        new(
            "application:api",
            "cloudshell-application-api",
            new ResourceWorkloadConfiguration(
                ResourceWorkloadKind.ContainerImage,
                "API",
                Image: "example/api:next",
                Replicas: replicas,
                ReplicasEnabled: replicas > 1));

    private static ResourceOrchestratorDeploymentApplyResult CreateApplyResult(
        string deploymentId,
        string revisionId,
        ResourceOrchestratorReplicaGroup? replicaGroup) =>
        new(
            new ResourceOrchestratorDeployment(
                deploymentId,
                "default",
                "application:api",
                "cloudshell-application-api",
                revisionId,
                new ResourceOrchestratorDeploymentSpec(
                    CreateService(replicas: 2),
                    revisionId),
                ResourceOrchestratorDeploymentStatus.Active),
            new ResourceOrchestratorRevision(
                new ResourceOrchestratorEnvironmentRevisionId("env-test-1"),
                deploymentId,
                "application:api",
                "cloudshell-application-api",
                RevisionNumber: 1,
                Now,
                ResourceOrchestratorRevisionStatus.Active,
                replicaGroup),
            ResourceProcedureResult.Completed("Applied deployment."));
}
