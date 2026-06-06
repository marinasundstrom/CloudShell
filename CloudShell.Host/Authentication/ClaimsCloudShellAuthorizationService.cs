using System.Security.Claims;
using CloudShell.Abstractions.Authorization;
using Microsoft.Extensions.Options;

namespace CloudShell.Host.Authentication;

public sealed class ClaimsCloudShellAuthorizationService(
    IHttpContextAccessor httpContextAccessor,
    IOptions<CloudShellAuthenticationOptions> options) : ICloudShellAuthorizationService
{
    private readonly CloudShellAuthenticationOptions options = options.Value;
    private readonly ClaimsPrincipal user =
        httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal();

    private ClaimsPrincipal User => user;

    public bool IsAuthenticated =>
        !options.Enabled || User.Identity?.IsAuthenticated == true;

    public bool HasPermission(string permission) =>
        !options.Enabled ||
        IsAuthenticated && HasClaim(CloudShellAuthenticationOptions.PermissionClaimType, permission);

    public bool CanAccessResourceGroup(string? resourceGroupId, string permission)
    {
        if (!options.Enabled)
        {
            return true;
        }

        if (!HasPermission(permission))
        {
            return false;
        }

        var scope = resourceGroupId ?? CloudShellAuthenticationOptions.UngroupedScope;
        return HasClaim(CloudShellAuthenticationOptions.ResourceGroupClaimType, scope);
    }

    public bool CanAccessResource(string resourceId, string? resourceGroupId, string permission)
    {
        if (!options.Enabled)
        {
            return true;
        }

        if (!HasPermission(permission))
        {
            return false;
        }

        return HasClaim(CloudShellAuthenticationOptions.ResourceClaimType, resourceId) ||
               HasClaim(
                   CloudShellAuthenticationOptions.ResourceGroupClaimType,
                   resourceGroupId ?? CloudShellAuthenticationOptions.UngroupedScope);
    }

    private bool HasClaim(string claimType, string value) =>
        User.Claims
            .Where(claim => string.Equals(claim.Type, claimType, StringComparison.OrdinalIgnoreCase))
            .Any(claim =>
                string.Equals(claim.Value, CloudShellPermissions.All, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(claim.Value, value, StringComparison.OrdinalIgnoreCase)) ||
        HasRoleGrant(claimType, value);

    private bool HasRoleGrant(string claimType, string value)
    {
        var grants = claimType switch
        {
            CloudShellAuthenticationOptions.PermissionClaimType => options.RolePermissions,
            CloudShellAuthenticationOptions.ResourceGroupClaimType => options.RoleResourceGroups,
            _ => null
        };

        if (grants is null)
        {
            return false;
        }

        var roles = User.Claims
            .Where(claim =>
                string.Equals(claim.Type, options.RoleClaimType, StringComparison.OrdinalIgnoreCase) ||
                User.Identities.Any(identity =>
                    string.Equals(identity.RoleClaimType, claim.Type, StringComparison.OrdinalIgnoreCase)))
            .Select(claim => claim.Value);

        return roles.Any(role =>
            grants.GetValueOrDefault(role, [])
                .Any(grant =>
                    string.Equals(grant, CloudShellPermissions.All, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(grant, value, StringComparison.OrdinalIgnoreCase)));
    }
}
