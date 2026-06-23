using CloudShell.Providers.Applications;

namespace CloudShell.Abstractions.Tests;

public sealed class ContainerApplicationScalingPlannerTests
{
    [Fact]
    public void PlanReplicaUpdate_UpdatesReplicaIntent()
    {
        var planner = new ContainerApplicationScalingPlanner();
        var application = new ApplicationResourceDefinition(
            "application:api",
            "API",
            string.Empty,
            containerImage: "example/api:latest",
            replicas: 1,
            resourceType: ApplicationResourceTypes.ContainerApp,
            replicasEnabled: false);

        var plan = planner.PlanReplicaUpdate(application, 3, definition => definition);

        Assert.Equal(3, plan.Definition.Replicas);
        Assert.True(plan.Definition.ReplicasEnabled);
    }

    [Fact]
    public void PlanReplicaUpdate_UsesNormalizer()
    {
        var planner = new ContainerApplicationScalingPlanner();
        var application = new ApplicationResourceDefinition(
            "application:api",
            "API",
            string.Empty,
            containerImage: "example/api:latest",
            resourceType: ApplicationResourceTypes.ContainerApp);

        var plan = planner.PlanReplicaUpdate(
            application,
            3,
            definition => definition with { Name = "Normalized API" });

        Assert.Equal("Normalized API", plan.Definition.Name);
        Assert.Equal(3, plan.Definition.Replicas);
    }

    [Fact]
    public void PlanReplicaUpdate_RejectsInvalidReplicaCount()
    {
        var planner = new ContainerApplicationScalingPlanner();
        var application = new ApplicationResourceDefinition(
            "application:api",
            "API",
            string.Empty,
            containerImage: "example/api:latest",
            resourceType: ApplicationResourceTypes.ContainerApp);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            planner.PlanReplicaUpdate(application, 0, definition => definition));

        Assert.Equal("replicas", exception.ParamName);
    }
}
