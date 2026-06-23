using CloudShell.Providers.Applications;

namespace CloudShell.Abstractions.Tests;

public sealed class ContainerApplicationRuntimeRevisionPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 23, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ShouldUseRevisionScopedRuntimeInstances_ReturnsFalseWhenRevisionIsMissing()
    {
        var policy = new ContainerApplicationRuntimeRevisionPolicy();

        Assert.False(policy.ShouldUseRevisionScopedRuntimeInstances(
            CreateApplication(),
            revision: null,
            revisionHistory: []));
    }

    [Fact]
    public void ShouldUseRevisionScopedRuntimeInstances_ReturnsTrueWhenApplicationTracksEnvironmentRevision()
    {
        var policy = new ContainerApplicationRuntimeRevisionPolicy();

        Assert.True(policy.ShouldUseRevisionScopedRuntimeInstances(
            CreateApplication() with { DeploymentEnvironmentRevisionId = "env-r1" },
            "rev-1",
            revisionHistory: []));
    }

    [Fact]
    public void ShouldUseRevisionScopedRuntimeInstances_ReturnsTrueForActiveRevisionHistory()
    {
        var policy = new ContainerApplicationRuntimeRevisionPolicy();

        Assert.True(policy.ShouldUseRevisionScopedRuntimeInstances(
            CreateApplication(),
            "rev-1",
            [
                new ApplicationContainerRevisionHistoryEntry(
                    "rev-1",
                    "application:api",
                    "example/api:latest",
                    2,
                    Now,
                    ApplicationContainerRevisionStatuses.Active,
                    ApplicationContainerRevisionChangeKinds.ImageDeployment)
            ]));
    }

    [Fact]
    public void ShouldUseRevisionScopedRuntimeInstances_ReturnsFalseForSupersededRevisionHistory()
    {
        var policy = new ContainerApplicationRuntimeRevisionPolicy();

        Assert.False(policy.ShouldUseRevisionScopedRuntimeInstances(
            CreateApplication(),
            "rev-1",
            [
                new ApplicationContainerRevisionHistoryEntry(
                    "rev-1",
                    "application:api",
                    "example/api:latest",
                    2,
                    Now,
                    ApplicationContainerRevisionStatuses.Superseded,
                    ApplicationContainerRevisionChangeKinds.ImageDeployment)
            ]));
    }

    private static ApplicationResourceDefinition CreateApplication() =>
        new(
            "application:api",
            "API",
            string.Empty,
            containerImage: "example/api:latest",
            resourceType: ApplicationResourceTypes.ContainerApp,
            containerRevision: "rev-1");
}
