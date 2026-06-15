using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Abstractions.Tests;

public sealed class ResourceMetadataTests
{
    [Fact]
    public void Resource_DefaultsToUserManagedNormalVisibility()
    {
        var resource = CreateResource("application:api");

        Assert.Equal(ResourceSource.User, resource.Source);
        Assert.Equal(ResourceManagementMode.UserManaged, resource.ManagementMode);
        Assert.Equal(ResourceVisibility.Normal, resource.Visibility);
        Assert.Equal(ResourceCleanupBehavior.None, resource.CleanupBehavior);
        Assert.Null(resource.OwnerResourceId);
        Assert.True(resource.IsNormalResource);
        Assert.False(resource.IsRuntimeManaged);
    }

    [Fact]
    public void Resource_IdentifiesRuntimeManagedHiddenResources()
    {
        var resource = CreateResource("container:api-1") with
        {
            Source = ResourceSource.RuntimeController,
            ManagementMode = ResourceManagementMode.RuntimeManaged,
            Visibility = ResourceVisibility.Hidden,
            OwnerResourceId = "application:api",
            CleanupBehavior = ResourceCleanupBehavior.DeleteWithOwner
        };

        Assert.False(resource.IsNormalResource);
        Assert.True(resource.IsRuntimeManaged);
        Assert.Equal("application:api", resource.OwnerResourceId);
    }

    private static Resource CreateResource(string id) =>
        new(
            id,
            id,
            "Test",
            "test",
            "local",
            ResourceState.Running,
            [],
            "1.0",
            DateTimeOffset.UtcNow,
            []);
}
