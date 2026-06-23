using CloudShell.Providers.Applications;

namespace CloudShell.Abstractions.Tests;

public sealed class ApplicationContainerRevisionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 23, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AppendRevision_AddsMissingBasedOnRevisionAndAssignsNumbers()
    {
        var service = new ApplicationContainerRevisionService(() => Now);
        var application = CreateApplication() with
        {
            ContainerImage = "example/api:v1",
            ContainerRevision = "rev-1",
            Replicas = 3,
            ContainerRevisions = []
        };

        var revisions = service.AppendRevision(
            application,
            "rev-2",
            "example/api:v2",
            4,
            ApplicationContainerRevisionChangeKinds.ImageDeployment,
            " operator ");

        Assert.Collection(
            revisions.OrderBy(revision => revision.RevisionNumber),
            revision =>
            {
                Assert.Equal("rev-1", revision.Id);
                Assert.Equal("example/api:v1", revision.Image);
                Assert.Equal(3, revision.RequestedReplicas);
                Assert.Equal(ApplicationContainerRevisionChangeKinds.Initial, revision.ChangeKind);
                Assert.Equal(1, revision.RevisionNumber);
            },
            revision =>
            {
                Assert.Equal("rev-2", revision.Id);
                Assert.Equal("example/api:v2", revision.Image);
                Assert.Equal(4, revision.RequestedReplicas);
                Assert.Equal("rev-1", revision.BasedOnRevisionId);
                Assert.Equal("operator", revision.ProvisionedBy);
                Assert.Equal(2, revision.RevisionNumber);
            });
    }

    [Fact]
    public void AppendRevision_ReplacesExistingRevisionAndClampsReplicas()
    {
        var service = new ApplicationContainerRevisionService(() => Now);
        var earlier = Now.AddMinutes(-10);
        var application = CreateApplication() with
        {
            ContainerRevision = "rev-1",
            ContainerRevisions =
            [
                new ApplicationContainerRevision(
                    "rev-1",
                    "example/api:v1",
                    2,
                    earlier,
                    ApplicationContainerRevisionChangeKinds.Initial),
                new ApplicationContainerRevision(
                    "rev-2",
                    "example/api:old",
                    2,
                    earlier.AddMinutes(1),
                    ApplicationContainerRevisionChangeKinds.ImageDeployment)
            ]
        };

        var revisions = service.AppendRevision(
            application,
            "rev-2",
            "example/api:v2",
            0,
            ApplicationContainerRevisionChangeKinds.RestoreDeployment,
            null);

        var revision = Assert.Single(revisions, candidate => candidate.Id == "rev-2");
        Assert.Equal("example/api:v2", revision.Image);
        Assert.Equal(1, revision.RequestedReplicas);
        Assert.Equal(ApplicationContainerRevisionChangeKinds.RestoreDeployment, revision.ChangeKind);
        Assert.Equal("rev-1", revision.BasedOnRevisionId);
        Assert.Equal(2, revision.RevisionNumber);
        Assert.Equal(2, revisions.Count);
    }

    [Fact]
    public void CreateHistoryEntries_ProjectsActiveAndSupersededRevisions()
    {
        var service = new ApplicationContainerRevisionService(() => Now);
        var application = CreateApplication() with
        {
            ContainerImage = "example/api:fallback",
            ContainerRevision = "rev-2",
            ContainerRevisions =
            [
                new ApplicationContainerRevision(
                    "rev-1",
                    "",
                    0,
                    Now.AddMinutes(-2),
                    "",
                    RevisionNumber: 1),
                new ApplicationContainerRevision(
                    "rev-2",
                    "example/api:v2",
                    2,
                    Now.AddMinutes(-1),
                    ApplicationContainerRevisionChangeKinds.ImageDeployment,
                    BasedOnRevisionId: "rev-1",
                    ProvisionedBy: "operator",
                    RevisionNumber: 2)
            ]
        };

        var entries = service.CreateHistoryEntries(application);

        Assert.Collection(
            entries.OrderBy(entry => entry.RevisionNumber),
            entry =>
            {
                Assert.Equal("rev-1", entry.Id);
                Assert.Equal(ApplicationContainerRevisionStatuses.Superseded, entry.Status);
                Assert.Equal("example/api:fallback", entry.Image);
                Assert.Equal(1, entry.RequestedReplicas);
                Assert.Equal(ApplicationContainerRevisionChangeKinds.ImageDeployment, entry.ChangeKind);
            },
            entry =>
            {
                Assert.Equal("rev-2", entry.Id);
                Assert.Equal(ApplicationContainerRevisionStatuses.Active, entry.Status);
                Assert.Equal("rev-1", entry.BasedOnRevisionId);
                Assert.Equal("operator", entry.ProvisionedBy);
            });
    }

    [Fact]
    public void CreateBasedOnHistoryEntry_UsesExistingRevisionOrFallbackState()
    {
        var service = new ApplicationContainerRevisionService(() => Now);
        var application = CreateApplication() with
        {
            ContainerImage = "example/api:v1",
            Replicas = 2,
            ContainerRevisions =
            [
                new ApplicationContainerRevision(
                    "rev-1",
                    "example/api:known",
                    3,
                    Now.AddMinutes(-5),
                    ApplicationContainerRevisionChangeKinds.ImageDeployment,
                    BasedOnRevisionId: "rev-0",
                    ProvisionedBy: "operator",
                    RevisionNumber: 7)
            ]
        };

        var known = service.CreateBasedOnHistoryEntry(application, "rev-1");
        var fallback = service.CreateBasedOnHistoryEntry(application, "rev-missing");

        Assert.NotNull(known);
        Assert.Equal("example/api:known", known.Image);
        Assert.Equal(3, known.RequestedReplicas);
        Assert.Equal("rev-0", known.BasedOnRevisionId);
        Assert.Equal("operator", known.ProvisionedBy);
        Assert.Equal(7, known.RevisionNumber);

        Assert.NotNull(fallback);
        Assert.Equal("example/api:v1", fallback.Image);
        Assert.Equal(2, fallback.RequestedReplicas);
        Assert.Equal(ApplicationContainerRevisionChangeKinds.Initial, fallback.ChangeKind);
        Assert.Equal(0, fallback.RevisionNumber);
    }

    [Theory]
    [InlineData(null, "unrevisioned")]
    [InlineData(" ", "unrevisioned")]
    [InlineData(" rev-1 ", "rev-1")]
    public void GetEffectiveRevision_NormalizesConfiguredRevision(
        string? revision,
        string expected)
    {
        var service = new ApplicationContainerRevisionService(() => Now);

        Assert.Equal(expected, service.GetEffectiveRevision(CreateApplication() with
        {
            ContainerRevision = revision
        }));
    }

    private static ApplicationResourceDefinition CreateApplication() =>
        new(
            "application:api",
            "api",
            executablePath: "api",
            resourceType: ApplicationResourceTypes.ContainerApp);
}
