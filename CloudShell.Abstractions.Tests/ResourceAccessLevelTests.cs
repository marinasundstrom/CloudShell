using CloudShell.Abstractions.Authorization;

namespace CloudShell.Abstractions.Tests;

public sealed class ResourceAccessLevelTests
{
    [Fact]
    public void GetResourceAccessLevel_ReturnsNoneWithoutMatchingPermission()
    {
        var authorization = new TestAuthorizationService();

        var accessLevel = authorization.GetResourceAccessLevel("application:api", null);

        Assert.Equal(ResourceAccessLevel.None, accessLevel);
        Assert.False(accessLevel.AllowsReference());
        Assert.False(accessLevel.AllowsRead());
        Assert.False(accessLevel.AllowsOperate());
        Assert.False(accessLevel.AllowsManage());
    }

    [Fact]
    public void GetResourceAccessLevel_ReturnsReferenceForReferencePermission()
    {
        var authorization = new TestAuthorizationService(
            ("application:api", CloudShellPermissions.Resources.Reference));

        var accessLevel = authorization.GetResourceAccessLevel("application:api", null);

        Assert.Equal(ResourceAccessLevel.Reference, accessLevel);
        Assert.True(accessLevel.AllowsReference());
        Assert.False(accessLevel.AllowsRead());
    }

    [Fact]
    public void GetResourceAccessLevel_ReturnsReadForReadPermission()
    {
        var authorization = new TestAuthorizationService(
            ("application:api", CloudShellPermissions.Resources.Read));

        var accessLevel = authorization.GetResourceAccessLevel("application:api", null);

        Assert.Equal(ResourceAccessLevel.Read, accessLevel);
        Assert.True(accessLevel.AllowsReference());
        Assert.True(accessLevel.AllowsRead());
        Assert.False(accessLevel.AllowsOperate());
    }

    [Fact]
    public void GetResourceAccessLevel_ReturnsOperateForActionPermission()
    {
        var authorization = new TestAuthorizationService(
            ("application:api", CommonResourceOperationPermissions.LifecycleAction));

        var accessLevel = authorization.GetResourceAccessLevel(
            "application:api",
            null,
            [CommonResourceOperationPermissions.LifecycleAction]);

        Assert.Equal(ResourceAccessLevel.Operate, accessLevel);
        Assert.True(accessLevel.AllowsRead());
        Assert.True(accessLevel.AllowsOperate());
        Assert.False(accessLevel.AllowsManage());
    }

    [Fact]
    public void GetResourceAccessLevel_ReturnsManageForManagePermission()
    {
        var authorization = new TestAuthorizationService(
            ("application:api", CloudShellPermissions.Resources.Manage));

        var accessLevel = authorization.GetResourceAccessLevel(
            "application:api",
            null,
            [CommonResourceOperationPermissions.LifecycleAction]);

        Assert.Equal(ResourceAccessLevel.Manage, accessLevel);
        Assert.True(accessLevel.AllowsReference());
        Assert.True(accessLevel.AllowsRead());
        Assert.True(accessLevel.AllowsOperate());
        Assert.True(accessLevel.AllowsManage());
    }

    private sealed class TestAuthorizationService(
        params (string ResourceId, string Permission)[] grants) : ICloudShellAuthorizationService
    {
        public bool IsAuthenticated => true;

        public bool HasPermission(string permission) => false;

        public bool CanAccessResourceGroup(string? resourceGroupId, string permission) => false;

        public bool CanAccessResource(string resourceId, string? resourceGroupId, string permission) =>
            grants.Any(grant =>
                string.Equals(grant.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(grant.Permission, permission, StringComparison.OrdinalIgnoreCase));
    }
}
