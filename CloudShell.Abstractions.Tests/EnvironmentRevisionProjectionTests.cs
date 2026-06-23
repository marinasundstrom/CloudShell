using CloudShell.Abstractions.ResourceManager;
using CloudShell.Hosting.ResourceManager;

namespace CloudShell.Abstractions.Tests;

public sealed class EnvironmentRevisionProjectionTests
{
    [Fact]
    public void CreateRows_ReturnsBaselineRevisionForDeclaredResourcesWithoutDeployments()
    {
        var rows = EnvironmentRevisionProjection.CreateRows(
            [
                CreateResource("application:api"),
                CreateResource("configuration:store")
            ]);

        var row = Assert.Single(rows);
        Assert.Equal(EnvironmentRevisionProjection.BaselineRevisionId, row.EnvironmentRevisionId);
        Assert.Equal(2, row.ResourceCount);
        Assert.Equal(0, row.DeploymentCount);
        Assert.Equal(0, row.ServiceCount);
        Assert.Equal(0, row.ReplicaGroupCount);
        Assert.Equal("Declared", row.LatestStatus);
        Assert.Equal("Declared resource graph", row.Description);
    }

    [Fact]
    public void CreateRows_UsesProjectedEnvironmentRevisionsWhenDeploymentsExist()
    {
        var rows = EnvironmentRevisionProjection.CreateRows(
            [
                CreateResource(
                    "application:api",
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [ResourceAttributeNames.DeploymentId] = "api-deployment",
                        [ResourceAttributeNames.DeploymentStatus] = "active",
                        [ResourceAttributeNames.DeploymentEnvironmentRevisionId] = "env-api-1",
                        [ResourceAttributeNames.DeploymentServiceId] = "api-service",
                        [ResourceAttributeNames.DeploymentReplicaGroupId] = "api-service-revision-1-replicas"
                    }),
                CreateResource("configuration:store")
            ]);

        Assert.Collection(
            rows,
            baseline =>
            {
                Assert.Equal(EnvironmentRevisionProjection.BaselineRevisionId, baseline.EnvironmentRevisionId);
                Assert.Equal(2, baseline.ResourceCount);
                Assert.Equal("Declared", baseline.LatestStatus);
            },
            projected =>
            {
                Assert.Equal("env-api-1", projected.EnvironmentRevisionId);
                Assert.Equal(1, projected.ResourceCount);
                Assert.Equal(1, projected.DeploymentCount);
                Assert.Equal(1, projected.ServiceCount);
                Assert.Equal(1, projected.ReplicaGroupCount);
                Assert.Equal("active", projected.LatestStatus);
                Assert.Equal("Deployment-produced environment revision", projected.Description);
            });
    }

    private static Resource CreateResource(
        string id,
        IReadOnlyDictionary<string, string>? attributes = null)
    {
        var name = id[(id.IndexOf(':', StringComparison.Ordinal) + 1)..];
        return new Resource(
            id,
            name,
            "test",
            "Test",
            "local",
            ResourceState.Running,
            [],
            "1",
            DateTimeOffset.UtcNow,
            [],
            Attributes: attributes);
    }
}
