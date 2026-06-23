using CloudShell.Abstractions.ResourceManager;
using CloudShell.Providers.Applications;

namespace CloudShell.Abstractions.Tests;

public sealed class ContainerApplicationDeploymentAppliedPlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 23, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PlanAppliedDeployment_RecordsEnvironmentRevisionId()
    {
        var planner = new ContainerApplicationDeploymentAppliedPlanner();

        var updated = planner.PlanAppliedDeployment(
            CreateApplication(),
            CreateApplyResult(new ResourceOrchestratorEnvironmentRevisionId("env-api-1")),
            definition => definition);

        Assert.NotNull(updated);
        Assert.Equal("env-api-1", updated.DeploymentEnvironmentRevisionId);
    }

    [Fact]
    public void PlanAppliedDeployment_UsesNormalizer()
    {
        var planner = new ContainerApplicationDeploymentAppliedPlanner();

        var updated = planner.PlanAppliedDeployment(
            CreateApplication(),
            CreateApplyResult(new ResourceOrchestratorEnvironmentRevisionId("env-api-1")),
            definition => definition with { Name = "Normalized API" });

        Assert.NotNull(updated);
        Assert.Equal("Normalized API", updated.Name);
        Assert.Equal("env-api-1", updated.DeploymentEnvironmentRevisionId);
    }

    [Fact]
    public void PlanAppliedDeployment_ReturnsNullWhenEnvironmentRevisionIdIsEmpty()
    {
        var planner = new ContainerApplicationDeploymentAppliedPlanner();

        var updated = planner.PlanAppliedDeployment(
            CreateApplication(),
            CreateApplyResult(default),
            definition => definition);

        Assert.Null(updated);
    }

    private static ApplicationResourceDefinition CreateApplication() =>
        new(
            "application:api",
            "API",
            string.Empty,
            containerImage: "example/api:latest",
            resourceType: ApplicationResourceTypes.ContainerApp,
            containerRevision: "rev-1");

    private static ResourceOrchestratorDeploymentApplyResult CreateApplyResult(
        ResourceOrchestratorEnvironmentRevisionId revisionId) =>
        new(
            new ResourceOrchestratorDeployment(
                "deployment-1",
                "default",
                "application:api",
                "cloudshell-application-api",
                "rev-1",
                new ResourceOrchestratorDeploymentSpec(
                    new ResourceOrchestratorService(
                        "application:api",
                        "cloudshell-application-api",
                        new ResourceWorkloadConfiguration(
                            ResourceWorkloadKind.ContainerImage,
                            "API",
                            Image: "example/api:latest")),
                    "rev-1"),
                ResourceOrchestratorDeploymentStatus.Active),
            new ResourceOrchestratorRevision(
                revisionId,
                "deployment-1",
                "application:api",
                "cloudshell-application-api",
                RevisionNumber: 1,
                Now,
                ResourceOrchestratorRevisionStatus.Active),
            ResourceProcedureResult.Completed("Applied deployment."));
}
