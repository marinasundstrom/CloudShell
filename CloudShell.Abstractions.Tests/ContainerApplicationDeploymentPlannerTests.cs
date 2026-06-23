using CloudShell.Providers.Applications;

namespace CloudShell.Abstractions.Tests;

public sealed class ContainerApplicationDeploymentPlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 23, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PlanImageDeployment_CreatesDefinitionAndDeploymentRecords()
    {
        var planner = new ContainerApplicationDeploymentPlanner(
            () => Now,
            () => "rev-2",
            () => "dep-1");
        var application = new ApplicationResourceDefinition(
            "application:api",
            "API",
            string.Empty,
            containerImage: "example/api:latest",
            replicas: 3,
            resourceType: ApplicationResourceTypes.ContainerApp,
            containerRevision: "rev-1",
            replicasEnabled: true,
            containerRevisions:
            [
                new ApplicationContainerRevision(
                    "rev-1",
                    "example/api:latest",
                    3,
                    Now.AddMinutes(-10),
                    ApplicationContainerRevisionChangeKinds.Initial,
                    RevisionNumber: 1)
            ]);

        var plan = planner.PlanImageDeployment(
            application,
            " example/api:20260623 ",
            2,
            requestedReplicasSpecified: true,
            triggeredBy: "build-server",
            orchestratorDeploymentId: "cloudshell-application-api-deployment",
            definition => definition);

        Assert.Equal("example/api:20260623", plan.Definition.ContainerImage);
        Assert.Null(plan.Definition.ContainerBuildContext);
        Assert.Null(plan.Definition.ContainerDockerfile);
        Assert.Equal("rev-2", plan.Definition.ContainerRevision);
        Assert.Equal(2, plan.Definition.Replicas);
        Assert.True(plan.Definition.ReplicasEnabled);
        Assert.Contains(plan.Definition.ContainerRevisions, revision =>
            revision.Id == "rev-2" &&
            revision.BasedOnRevisionId == "rev-1" &&
            revision.ProvisionedBy == "build-server" &&
            revision.RevisionNumber == 2);

        Assert.Equal("dep-1", plan.Deployment.Id);
        Assert.Equal("application:api", plan.Deployment.ApplicationId);
        Assert.Equal("rev-2", plan.Deployment.RevisionId);
        Assert.Equal("rev-1", plan.Deployment.BasedOnRevisionId);
        Assert.Equal("example/api:20260623", plan.Deployment.Image);
        Assert.Equal(2, plan.Deployment.RequestedReplicas);
        Assert.Equal(Now, plan.Deployment.CreatedAt);
        Assert.Equal(ApplicationContainerDeploymentStatuses.Completed, plan.Deployment.Status);
        Assert.Equal("build-server", plan.Deployment.TriggeredBy);
        Assert.Equal("cloudshell-application-api-deployment", plan.Deployment.OrchestratorDeploymentId);

        Assert.Equal("rev-2", plan.Revision.Id);
        Assert.Equal(ApplicationContainerRevisionStatuses.Active, plan.Revision.Status);
        Assert.Equal(2, plan.Revision.RevisionNumber);
        Assert.NotNull(plan.BasedOnRevision);
        Assert.Equal("rev-1", plan.BasedOnRevision.Id);
        Assert.Equal(ApplicationContainerRevisionStatuses.Superseded, plan.BasedOnRevision.Status);
    }

    [Fact]
    public void PlanImageDeployment_PreservesReplicaModeWhenReplicaCountIsNotSpecified()
    {
        var planner = new ContainerApplicationDeploymentPlanner(
            () => Now,
            () => "rev-2",
            () => "dep-1");
        var application = new ApplicationResourceDefinition(
            "application:api",
            "API",
            string.Empty,
            containerImage: "example/api:latest",
            resourceType: ApplicationResourceTypes.ContainerApp,
            containerRevision: "rev-1",
            replicasEnabled: false);

        var plan = planner.PlanImageDeployment(
            application,
            "example/api:20260623",
            1,
            requestedReplicasSpecified: false,
            triggeredBy: null,
            orchestratorDeploymentId: "cloudshell-application-api-deployment",
            definition => definition);

        Assert.False(plan.Definition.ReplicasEnabled);
        Assert.Equal(1, plan.Definition.Replicas);
    }
}
