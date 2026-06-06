using System.Security.Claims;
using CloudShell.Abstractions.Authorization;
using CloudShell.ControlPlane.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace CloudShell.Abstractions.Tests;

public sealed class AuthorizationTests
{
    [Fact]
    public void RoleGrant_RequiresBothPermissionAndResourceGroupScope()
    {
        var options = new CloudShellAuthenticationOptions
        {
            RolePermissions = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Team.Reader"] = [CloudShellPermissions.Resources.Read]
            },
            RoleResourceGroups = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Team.Reader"] = ["group-a"]
            }
        };
        var authorization = CreateAuthorization(
            options,
            new Claim(ClaimTypes.Role, "Team.Reader"));

        Assert.True(authorization.CanAccessResourceGroup(
            "group-a",
            CloudShellPermissions.Resources.Read));
        Assert.False(authorization.CanAccessResourceGroup(
            "group-b",
            CloudShellPermissions.Resources.Read));
        Assert.False(authorization.CanAccessResourceGroup(
            "group-a",
            CloudShellPermissions.Resources.Manage));
    }

    [Fact]
    public void DirectPermissionAndScopeClaims_GrantResourceAccess()
    {
        var authorization = CreateAuthorization(
            new CloudShellAuthenticationOptions(),
            new Claim(
                CloudShellAuthenticationOptions.PermissionClaimType,
                CloudShellPermissions.Resources.Manage),
            new Claim(
                CloudShellAuthenticationOptions.ResourceGroupClaimType,
                "group-a"));

        Assert.True(authorization.CanAccessResource(
            "resource-a",
            "group-a",
            CloudShellPermissions.Resources.Manage));
        Assert.False(authorization.CanAccessResource(
            "resource-b",
            "group-b",
            CloudShellPermissions.Resources.Manage));
    }

    [Fact]
    public void DirectResourceScope_DoesNotGrantAccessToOtherResources()
    {
        var authorization = CreateAuthorization(
            new CloudShellAuthenticationOptions(),
            new Claim(
                CloudShellAuthenticationOptions.PermissionClaimType,
                CloudShellPermissions.Resources.Read),
            new Claim(
                CloudShellAuthenticationOptions.ResourceClaimType,
                "resource-a"));

        Assert.True(authorization.CanAccessResource(
            "resource-a",
            null,
            CloudShellPermissions.Resources.Read));
        Assert.False(authorization.CanAccessResource(
            "resource-b",
            null,
            CloudShellPermissions.Resources.Read));
    }

    [Fact]
    public void DisabledAuthentication_AllowsAccess()
    {
        var authorization = CreateAuthorization(
            new CloudShellAuthenticationOptions { Enabled = false },
            authenticated: false);

        Assert.True(authorization.HasPermission(CloudShellPermissions.Resources.Manage));
        Assert.True(authorization.CanAccessResourceGroup(
            "any-group",
            CloudShellPermissions.ResourceGroups.Manage));
    }

    private static ClaimsCloudShellAuthorizationService CreateAuthorization(
        CloudShellAuthenticationOptions options,
        params Claim[] claims) =>
        CreateAuthorization(options, true, claims);

    private static ClaimsCloudShellAuthorizationService CreateAuthorization(
        CloudShellAuthenticationOptions options,
        bool authenticated,
        params Claim[] claims)
    {
        var identity = new ClaimsIdentity(
            claims,
            authenticated ? "Test" : null,
            ClaimTypes.Name,
            options.RoleClaimType);
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity)
        };
        var accessor = new HttpContextAccessor { HttpContext = context };

        return new ClaimsCloudShellAuthorizationService(
            accessor,
            Options.Create(options));
    }
}
