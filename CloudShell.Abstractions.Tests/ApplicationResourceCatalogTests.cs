using CloudShell.Abstractions.ResourceManager;
using CloudShell.Providers.Applications;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace CloudShell.Abstractions.Tests;

public sealed class ApplicationResourceCatalogTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 23, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GetApplication_ResolvesStoredDefinition()
    {
        using var fixture = new CatalogFixture(new ResolveEndpointRule());
        fixture.Store.Save(new ApplicationResourceDefinition(
            "application:api",
            "API",
            "dotnet",
            resourceType: ApplicationResourceTypes.ExecutableApplication));

        var application = fixture.Catalog.GetApplication("application:api");

        Assert.NotNull(application);
        Assert.Equal("http://localhost:5011", application.Endpoint);
    }

    [Fact]
    public void GetContainerRevisions_UsesPersistedDeploymentHistoryWhenAvailable()
    {
        using var fixture = new CatalogFixture();
        fixture.Store.Save(CreateContainerApplication() with
        {
            ContainerRevision = "declared-rev",
            ContainerRevisions =
            [
                new ApplicationContainerRevision(
                    "declared-rev",
                    "example/api:declared",
                    1,
                    Now.AddMinutes(-10),
                    ApplicationContainerRevisionChangeKinds.Initial)
            ]
        });
        fixture.ContainerDeployments.RecordDeployment(
            new ApplicationContainerDeployment(
                "deployment-1",
                "application:api",
                "history-rev",
                "declared-rev",
                "example/api:history",
                3,
                Now,
                ApplicationContainerDeploymentStatuses.Completed,
                ApplicationContainerRevisionChangeKinds.ImageDeployment),
            new ApplicationContainerRevisionHistoryEntry(
                "history-rev",
                "application:api",
                "example/api:history",
                3,
                Now,
                ApplicationContainerRevisionStatuses.Active,
                ApplicationContainerRevisionChangeKinds.ImageDeployment,
                BasedOnRevisionId: "declared-rev"));

        var revisions = fixture.Catalog.GetContainerRevisions("application:api");

        var revision = Assert.Single(revisions);
        Assert.Equal("history-rev", revision.Id);
        Assert.Equal("example/api:history", revision.Image);
        Assert.Equal(3, revision.RequestedReplicas);
        Assert.Equal(1, revision.RevisionNumber);
    }

    [Fact]
    public void GetContainerRevisions_ProjectsDeclaredStateWhenNoDeploymentHistoryExists()
    {
        using var fixture = new CatalogFixture();
        fixture.Store.Save(CreateContainerApplication() with
        {
            ContainerRevision = "rev-2",
            ContainerRevisions =
            [
                new ApplicationContainerRevision(
                    "rev-1",
                    "example/api:v1",
                    2,
                    Now.AddMinutes(-5),
                    ApplicationContainerRevisionChangeKinds.Initial),
                new ApplicationContainerRevision(
                    "rev-2",
                    "example/api:v2",
                    4,
                    Now,
                    ApplicationContainerRevisionChangeKinds.ImageDeployment,
                    BasedOnRevisionId: "rev-1",
                    ProvisionedBy: "operator")
            ]
        });

        var revisions = fixture.Catalog.GetContainerRevisions("application:api")
            .OrderBy(revision => revision.RevisionNumber)
            .ToArray();

        Assert.Collection(
            revisions,
            revision =>
            {
                Assert.Equal("rev-1", revision.Id);
                Assert.Equal(ApplicationContainerRevisionStatuses.Superseded, revision.Status);
                Assert.Equal(1, revision.RevisionNumber);
            },
            revision =>
            {
                Assert.Equal("rev-2", revision.Id);
                Assert.Equal(ApplicationContainerRevisionStatuses.Active, revision.Status);
                Assert.Equal("rev-1", revision.BasedOnRevisionId);
                Assert.Equal("operator", revision.ProvisionedBy);
                Assert.Equal(2, revision.RevisionNumber);
            });
    }

    private static ApplicationResourceDefinition CreateContainerApplication() =>
        new(
            "application:api",
            "API",
            executablePath: string.Empty,
            containerImage: "example/api:latest",
            replicas: 2,
            resourceType: ApplicationResourceTypes.ContainerApp,
            replicasEnabled: true);

    private sealed class CatalogFixture : IDisposable
    {
        private readonly string _contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        public CatalogFixture(params IApplicationResourceDefinitionNormalizationRule[] rules)
        {
            var environment = new TestHostEnvironment(_contentRoot);
            var options = new ApplicationProviderOptions
            {
                DefinitionsPath = "application-resources.json",
                ContainerDeploymentHistoryPath = "application-container-deployments.json"
            };
            Normalizer = new ApplicationResourceDefinitionNormalizer(environment, rules);
            Store = new ApplicationResourceStore(options, environment, Normalizer);
            ContainerDeployments = new ApplicationContainerDeploymentStore(options, environment);
            Catalog = new ApplicationResourceCatalog(Store, ContainerDeployments, Normalizer);
        }

        public ApplicationResourceDefinitionNormalizer Normalizer { get; }

        public ApplicationResourceStore Store { get; }

        public ApplicationContainerDeploymentStore ContainerDeployments { get; }

        public ApplicationResourceCatalog Catalog { get; }

        public void Dispose()
        {
            if (Directory.Exists(_contentRoot))
            {
                Directory.Delete(_contentRoot, recursive: true);
            }
        }
    }

    private sealed class ResolveEndpointRule : IApplicationResourceDefinitionNormalizationRule
    {
        public bool AppliesTo(ApplicationResourceDefinition definition) => true;

        public ApplicationResourceDefinition Normalize(
            ApplicationResourceDefinition definition,
            ApplicationResourceDefinitionNormalizationContext context) =>
            definition;

        public ApplicationResourceDefinition Resolve(
            ApplicationResourceDefinition definition,
            ApplicationResourceDefinitionNormalizationContext context) =>
            definition with { Endpoint = "http://localhost:5011" };
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "CloudShell.Tests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
