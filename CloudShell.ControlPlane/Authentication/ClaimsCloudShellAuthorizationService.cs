using System.Security.Claims;
using CloudShell.Abstractions.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace CloudShell.ControlPlane.Authentication;

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

        if (HasResourcePermissionClaim(resourceId, permission))
        {
            return true;
        }

        if (!HasClaim(CloudShellAuthenticationOptions.PermissionClaimType, permission))
        {
            return false;
        }

        var groupScope = resourceGroupId ?? CloudShellAuthenticationOptions.UngroupedScope;
        if (HasClaim(CloudShellAuthenticationOptions.ResourceGroupClaimType, groupScope))
        {
            return true;
        }

        return !HasAnyResourcePermissionClaim() &&
               HasClaim(CloudShellAuthenticationOptions.ResourceClaimType, resourceId);
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

    private bool HasAnyResourcePermissionClaim() =>
        User.Claims.Any(claim =>
            string.Equals(
                claim.Type,
                CloudShellAuthenticationOptions.ResourcePermissionClaimType,
                StringComparison.OrdinalIgnoreCase));

    private bool HasResourcePermissionClaim(string resourceId, string permission) =>
        User.Claims
            .Where(claim => string.Equals(
                claim.Type,
                CloudShellAuthenticationOptions.ResourcePermissionClaimType,
                StringComparison.OrdinalIgnoreCase))
            .Any(claim => ResourcePermissionClaimMatches(claim.Value, resourceId, permission));

    private static bool ResourcePermissionClaimMatches(
        string value,
        string resourceId,
        string permission)
    {
        var separatorIndex = value.IndexOf(CloudShellAuthenticationOptions.ResourcePermissionClaimSeparator);
        if (separatorIndex <= 0 || separatorIndex == value.Length - 1)
        {
            return false;
        }

        var claimResourceId = value[..separatorIndex];
        var claimPermission = value[(separatorIndex + 1)..];
        return
            (string.Equals(claimResourceId, CloudShellPermissions.All, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(claimResourceId, resourceId, StringComparison.OrdinalIgnoreCase)) &&
            (string.Equals(claimPermission, CloudShellPermissions.All, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(claimPermission, permission, StringComparison.OrdinalIgnoreCase));
    }
}
