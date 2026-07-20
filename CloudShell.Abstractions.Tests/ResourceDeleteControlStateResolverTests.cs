using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Hosting.ResourceManager;

namespace CloudShell.Abstractions.Tests;

public sealed class ResourceDeleteControlStateResolverTests
{
    [Fact]
    public void Resolve_EnablesUserManagedResourceWhenProviderAllowsDelete()
    {
        var state = ResourceDeleteControlStateResolver.Resolve(
            CreateResource(),
            CreateCapabilities(canDelete: true),
            isReadOnly: false);

        Assert.True(state.IsEnabled);
        Assert.Equal("Delete", state.Title);
    }

    [Fact]
    public void Resolve_RejectsReadOnlyModeFirst()
    {
        var state = ResourceDeleteControlStateResolver.Resolve(
            CreateResource(ResourceSource.Provider, ResourceManagementMode.ProviderManaged),
            CreateCapabilities(canDelete: false),
            isReadOnly: true);

        Assert.False(state.IsEnabled);
        Assert.Equal(
            "Delete unavailable. Resource Manager is in read-only mode.",
            state.Title);
    }

    [Fact]
    public void Resolve_RejectsResourceThatIsNotUserManaged()
    {
        var state = ResourceDeleteControlStateResolver.Resolve(
            CreateResource(ResourceSource.Provider, ResourceManagementMode.ProviderManaged),
            CreateCapabilities(canDelete: true),
            isReadOnly: false);

        Assert.False(state.IsEnabled);
        Assert.Equal("Delete unavailable. Resource is not user-managed.", state.Title);
    }

    [Fact]
    public void Resolve_ExplainsProviderDeleteRestriction()
    {
        var state = ResourceDeleteControlStateResolver.Resolve(
            CreateResource(),
            CreateCapabilities(canDelete: false),
            isReadOnly: false);

        Assert.False(state.IsEnabled);
        Assert.Equal(
            "Delete unavailable. The provider does not allow this resource to be deleted.",
            state.Title);
    }

    [Fact]
    public void Resolve_RejectsDuplicateDelete()
    {
        var state = ResourceDeleteControlStateResolver.Resolve(
            CreateResource(),
            CreateCapabilities(canDelete: true),
            isReadOnly: false,
            isDeleting: true);

        Assert.False(state.IsEnabled);
        Assert.Equal(
            "Delete unavailable. The resource is already being deleted.",
            state.Title);
    }

    private static Resource CreateResource(
        ResourceSource source = ResourceSource.User,
        ResourceManagementMode managementMode = ResourceManagementMode.UserManaged) =>
        new(
            "application:api",
            "API",
            "application.aspnetcore",
            "sample",
            "local",
            ResourceState.Stopped,
            [],
            "1.0",
            DateTimeOffset.UtcNow,
            [],
            Source: source,
            ManagementMode: managementMode);

    private static ResourceOperationCapabilities CreateCapabilities(bool canDelete) =>
        new(
            "application:api",
            CanManage: true,
            CanDelete: canDelete,
            ExecutableActionIds: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            ResourceActionCapabilities: []);
}
