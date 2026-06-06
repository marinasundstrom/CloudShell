using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace CloudShell.Host.Authentication;

public sealed class CloudShellIdentitySeeder(
    RoleManager<IdentityRole> roleManager,
    IOptions<CloudShellAuthenticationOptions> options)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        foreach (var (roleName, permissions) in options.Value.RolePermissions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var role = await roleManager.FindByNameAsync(roleName);
            if (role is null)
            {
                role = new IdentityRole(roleName);
                var result = await roleManager.CreateAsync(role);
                ThrowIfFailed(result, $"create role '{roleName}'");
            }

            var desiredClaims = permissions
                .Select(permission => new Claim(
                    CloudShellAuthenticationOptions.PermissionClaimType,
                    permission))
                .Concat(options.Value.RoleResourceGroups
                    .GetValueOrDefault(roleName, [])
                    .Select(resourceGroup => new Claim(
                        CloudShellAuthenticationOptions.ResourceGroupClaimType,
                        resourceGroup)))
                .ToArray();

            var currentClaims = await roleManager.GetClaimsAsync(role);
            foreach (var claim in currentClaims.Where(IsCloudShellClaim))
            {
                var result = await roleManager.RemoveClaimAsync(role, claim);
                ThrowIfFailed(result, $"remove stale claim from role '{roleName}'");
            }

            foreach (var claim in desiredClaims)
            {
                var result = await roleManager.AddClaimAsync(role, claim);
                ThrowIfFailed(result, $"add claim to role '{roleName}'");
            }
        }
    }

    private static bool IsCloudShellClaim(Claim claim) =>
        claim.Type is CloudShellAuthenticationOptions.PermissionClaimType or
            CloudShellAuthenticationOptions.ResourceGroupClaimType;

    private static void ThrowIfFailed(IdentityResult result, string operation)
    {
        if (result.Succeeded)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Failed to {operation}: {string.Join(", ", result.Errors.Select(error => error.Description))}");
    }
}
