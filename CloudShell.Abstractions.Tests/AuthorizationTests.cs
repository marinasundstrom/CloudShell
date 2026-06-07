using System.Security.Claims;
using CloudShell.Abstractions.Authorization;
using CloudShell.ControlPlane.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace CloudShell.Abstractions.Tests;

public sealed class AuthorizationTests
{
    [Fact]
    public void BuiltInAuthorityToken_RoundTripsClaims()
    {
        using var tokens = new BuiltInAuthorityTokenService(Options.Create(
            new CloudShellAuthenticationOptions
            {
                BuiltInAuthority =
                {
                    Enabled = true,
                    Issuer = "https://cloudshell.test",
                    Audience = "cloudshell-control-plane"
                }
            }));

        var token = tokens.IssueToken(
            [
                new Claim(ClaimTypes.NameIdentifier, "user-1"),
                new Claim(ClaimTypes.Name, "Ada"),
                new Claim(ClaimTypes.Role, "CloudShell.Reader"),
                new Claim(
                    CloudShellAuthenticationOptions.ResourceGroupClaimType,
                    "group-a")
            ],
            "cloudshell-control-plane",
            ["ControlPlane.Access"]);

        var principal = tokens.ValidateToken(token.AccessToken);

        Assert.NotNull(principal);
        Assert.Equal("Ada", principal.FindFirst(ClaimTypes.Name)?.Value);
        Assert.Contains(
            principal.Claims,
            claim => claim.Type == ClaimTypes.Role && claim.Value == "CloudShell.Reader");
        Assert.Contains(
            principal.Claims,
            claim => claim.Type == CloudShellAuthenticationOptions.ResourceGroupClaimType &&
                claim.Value == "group-a");
    }

    [Fact]
    public void BuiltInAuthorityToken_RejectsTamperedToken()
    {
        using var tokens = new BuiltInAuthorityTokenService(Options.Create(
            new CloudShellAuthenticationOptions
            {
                BuiltInAuthority =
                {
                    Enabled = true,
                    Issuer = "https://cloudshell.test",
                    Audience = "cloudshell-control-plane"
                }
            }));
        var token = tokens.IssueToken(
            [new Claim(ClaimTypes.NameIdentifier, "user-1")],
            "cloudshell-control-plane",
            ["ControlPlane.Access"]);
        var tampered = token.AccessToken[..^1] +
            (token.AccessToken[^1] == 'a' ? 'b' : 'a');

        Assert.Null(tokens.ValidateToken(tampered));
    }

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
    public void DefaultRoles_OnlyAdministratorCanConfigureShell()
    {
        var administrator = CreateAuthorization(
            new CloudShellAuthenticationOptions(),
            new Claim(ClaimTypes.Role, "CloudShell.Administrator"));
        var contributor = CreateAuthorization(
            new CloudShellAuthenticationOptions(),
            new Claim(ClaimTypes.Role, "CloudShell.Contributor"));
        var reader = CreateAuthorization(
            new CloudShellAuthenticationOptions(),
            new Claim(ClaimTypes.Role, "CloudShell.Reader"));

        Assert.True(administrator.HasPermission(CloudShellPermissions.Shell.Configure));
        Assert.True(contributor.HasPermission(CloudShellPermissions.Shell.Read));
        Assert.True(reader.HasPermission(CloudShellPermissions.Shell.Read));
        Assert.False(contributor.HasPermission(CloudShellPermissions.Shell.Configure));
        Assert.False(reader.HasPermission(CloudShellPermissions.Shell.Configure));
    }

    [Fact]
    public void DisabledAuthentication_AllowsAccess()
    {
        var authorization = CreateAuthorization(
            new CloudShellAuthenticationOptions { Enabled = false },
            authenticated: false);

        Assert.True(authorization.HasPermission(CloudShellPermissions.Shell.Configure));
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
