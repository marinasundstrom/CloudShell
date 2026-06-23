using CloudShell.Providers.Applications;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace CloudShell.Abstractions.Tests;

public sealed class ContainerApplicationDefinitionNormalizationRuleTests
{
    [Fact]
    public void SharedNormalizer_DoesNotCreateContainerAppRevisionState()
    {
        var normalizer = new ApplicationResourceDefinitionNormalizer(
            new TestHostEnvironment(Path.GetTempPath()));
        var definition = new ApplicationResourceDefinition(
            "application:api",
            "API",
            string.Empty,
            containerImage: "example/api:latest",
            replicas: 2,
            resourceType: ApplicationResourceTypes.ContainerApp);

        var normalized = normalizer.Normalize(definition);

        Assert.Equal(ContainerRegistryDefaults.Default, normalized.ContainerRegistry);
        Assert.Null(normalized.ContainerRevision);
        Assert.Empty(normalized.ContainerRevisions);
        Assert.False(normalized.ReplicasEnabled);
    }

    [Fact]
    public void ProviderComposedNormalizer_CreatesContainerAppRevisionState()
    {
        var normalizer = CreateContainerApplicationNormalizer();
        var definition = new ApplicationResourceDefinition(
            "application:api",
            "API",
            string.Empty,
            containerImage: "example/api:latest",
            replicas: 2,
            resourceType: ApplicationResourceTypes.ContainerApp);

        var normalized = normalizer.Normalize(definition);
        var revision = Assert.Single(normalized.ContainerRevisions);

        Assert.Equal(ContainerRegistryDefaults.Default, normalized.ContainerRegistry);
        Assert.StartsWith("rev-", normalized.ContainerRevision);
        Assert.Equal(normalized.ContainerRevision, revision.Id);
        Assert.Equal("example/api:latest", revision.Image);
        Assert.Equal(2, revision.RequestedReplicas);
        Assert.Equal(1, revision.RevisionNumber);
        Assert.Equal(ApplicationContainerRevisionChangeKinds.Initial, revision.ChangeKind);
        Assert.True(normalized.ReplicasEnabled);
    }

    [Fact]
    public void ProviderComposedNormalizer_ClearsContainerAppRevisionStateForNonContainerBackedApps()
    {
        var normalizer = CreateContainerApplicationNormalizer();
        var definition = new ApplicationResourceDefinition(
            "application:api",
            "API",
            string.Empty,
            replicas: 2,
            resourceType: ApplicationResourceTypes.ContainerApp,
            containerRevision: "rev-1",
            containerRevisions:
            [
                new ApplicationContainerRevision(
                    "rev-1",
                    "example/api:latest",
                    2,
                    DateTimeOffset.UtcNow,
                    ApplicationContainerRevisionChangeKinds.Initial)
            ],
            replicasEnabled: true);

        var normalized = normalizer.Normalize(definition);

        Assert.Null(normalized.ContainerRevision);
        Assert.Empty(normalized.ContainerRevisions);
        Assert.False(normalized.ReplicasEnabled);
    }

    private static ApplicationResourceDefinitionNormalizer CreateContainerApplicationNormalizer() =>
        new(
            new TestHostEnvironment(Path.GetTempPath()),
            [
                new ProjectBackedApplicationResourceDefinitionNormalizationRule(),
                new ContainerBackedApplicationResourceDefinitionNormalizationRule(),
                new ContainerApplicationDefinitionNormalizationRule()
            ]);

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "CloudShell.Tests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
