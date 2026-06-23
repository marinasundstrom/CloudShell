using CloudShell.Providers.Applications;

namespace CloudShell.Abstractions.Tests;

public sealed class ContainerApplicationDeploymentFailurePlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 23, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PlanApplyFailure_UsesDeploymentRecordAndRestoresBasedOnRevisionState()
    {
        var planner = new ContainerApplicationDeploymentFailurePlanner();
        var application = CreateApplication();
        var deployment = new ApplicationContainerDeployment(
            "dep-1",
            "application:api",
            "rev-2",
            "rev-1",
            "example/api:broken",
            1,
            Now,
            ApplicationContainerDeploymentStatuses.Completed,
            ApplicationContainerRevisionChangeKinds.ImageDeployment,
            OrchestratorDeploymentId: "orchestrator-dep");

        var plan = planner.PlanApplyFailure(
            application,
            "orchestrator-dep",
            "rev-2",
            deployment,
            definition => definition);

        Assert.Equal("dep-1", plan.DeploymentId);
        Assert.Equal("rev-2", plan.RevisionId);
        Assert.Equal("rev-1", plan.BasedOnRevisionId);
        Assert.NotNull(plan.RestoredDefinition);
        Assert.Equal("example/api:latest", plan.RestoredDefinition.ContainerImage);
        Assert.Equal("rev-1", plan.RestoredDefinition.ContainerRevision);
        Assert.Equal(3, plan.RestoredDefinition.Replicas);
        Assert.True(plan.RestoredDefinition.ReplicasEnabled);
        Assert.DoesNotContain(plan.RestoredDefinition.ContainerRevisions, revision => revision.Id == "rev-2");
    }

    [Fact]
    public void PlanApplyFailure_FallsBackToRevisionBaseWhenDeploymentRecordIsMissing()
    {
        var planner = new ContainerApplicationDeploymentFailurePlanner();

        var plan = planner.PlanApplyFailure(
            CreateApplication(),
            "orchestrator-dep",
            "rev-2",
            appDeployment: null,
            definition => definition);

        Assert.Equal("orchestrator-dep", plan.DeploymentId);
        Assert.Equal("rev-1", plan.BasedOnRevisionId);
        Assert.NotNull(plan.RestoredDefinition);
        Assert.Equal("rev-1", plan.RestoredDefinition.ContainerRevision);
    }

    [Fact]
    public void PlanApplyFailure_DoesNotRestoreWhenBaseRevisionIsUnknown()
    {
        var planner = new ContainerApplicationDeploymentFailurePlanner();
        var application = CreateApplication() with
        {
            ContainerRevisions =
            [
                new ApplicationContainerRevision(
                    "rev-2",
                    "example/api:broken",
                    1,
                    Now,
                    ApplicationContainerRevisionChangeKinds.ImageDeployment,
                    BasedOnRevisionId: "rev-missing",
                    RevisionNumber: 2)
            ]
        };

        var plan = planner.PlanApplyFailure(
            application,
            "orchestrator-dep",
            "rev-2",
            appDeployment: null,
            definition => definition);

        Assert.Equal("rev-missing", plan.BasedOnRevisionId);
        Assert.Null(plan.RestoredDefinition);
    }

    private static ApplicationResourceDefinition CreateApplication() =>
        new(
            "application:api",
            "API",
            string.Empty,
            containerImage: "example/api:broken",
            replicas: 1,
            resourceType: ApplicationResourceTypes.ContainerApp,
            containerRevision: "rev-2",
            replicasEnabled: false,
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
                    "example/api:broken",
                    1,
                    Now,
                    ApplicationContainerRevisionChangeKinds.ImageDeployment,
                    BasedOnRevisionId: "rev-1",
                    RevisionNumber: 2)
            ]);
}
