using System.Security.Claims;
using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace CloudShell.ControlPlane.Authentication;

public sealed class CloudShellIdentitySeeder(
    UserManager<IdentityUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IOptions<CloudShellAuthenticationOptions> options,
    InMemoryIdentitySetupOptions inMemoryIdentity,
    ResourceDeclarationStore declarations)
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

        await SeedInMemoryUsersAsync(cancellationToken);
    }

    private async Task SeedInMemoryUsersAsync(CancellationToken cancellationToken)
    {
        if (!inMemoryIdentity.IsConfigured ||
            !inMemoryIdentity.UseAspNetCoreIdentityStore)
        {
            return;
        }

        var grantClaims = GetInMemoryUserGrantClaims();
        foreach (var configuredUser in inMemoryIdentity.Users)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var userName = configuredUser.UserName.Trim();
            var user = await userManager.FindByNameAsync(userName);
            if (user is null)
            {
                user = new IdentityUser
                {
                    UserName = userName,
                    Email = string.IsNullOrWhiteSpace(configuredUser.Email)
                        ? null
                        : configuredUser.Email.Trim(),
                    EmailConfirmed = !string.IsNullOrWhiteSpace(configuredUser.Email)
                };

                var createResult = string.IsNullOrWhiteSpace(configuredUser.Password)
                    ? await userManager.CreateAsync(user)
                    : await userManager.CreateAsync(user, configuredUser.Password);
                ThrowIfFailed(createResult, $"create in-memory user '{userName}'");
            }

            foreach (var role in configuredUser.Roles.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (await userManager.IsInRoleAsync(user, role))
                {
                    continue;
                }

                var roleResult = await userManager.AddToRoleAsync(user, role);
                ThrowIfFailed(roleResult, $"add in-memory user '{userName}' to role '{role}'");
            }

            var desiredClaims = configuredUser.Claims
                .Concat(grantClaims.GetValueOrDefault(
                    InMemoryIdentityUserOptions.CreatePrincipalId(userName),
                    []))
                .ToArray();
            var currentClaims = await userManager.GetClaimsAsync(user);
            foreach (var claim in desiredClaims)
            {
                if (currentClaims.Any(current => SameClaim(current, claim)))
                {
                    continue;
                }

                var claimResult = await userManager.AddClaimAsync(user, claim);
                ThrowIfFailed(claimResult, $"add claim '{claim.Type}' to in-memory user '{userName}'");
            }
        }
    }

    private static bool IsCloudShellClaim(Claim claim) =>
        claim.Type is CloudShellAuthenticationOptions.PermissionClaimType or
            CloudShellAuthenticationOptions.ResourceGroupClaimType;

    private IReadOnlyDictionary<string, IReadOnlyList<Claim>> GetInMemoryUserGrantClaims()
    {
        var claims = new Dictionary<string, List<Claim>>(StringComparer.OrdinalIgnoreCase);
        foreach (var grant in declarations.GetPermissionGrants())
        {
            if (grant.Principal.Kind != ResourcePrincipalKind.User ||
                !MatchesInMemoryProvider(grant.Principal.ProviderId) ||
                !inMemoryIdentity.Users.TryGetValue(grant.Principal.Id, out _))
            {
                continue;
            }

            if (!claims.TryGetValue(grant.Principal.Id, out var userClaims))
            {
                userClaims = [];
                claims[grant.Principal.Id] = userClaims;
            }

            userClaims.Add(new Claim(
                CloudShellAuthorizationClaimTypes.ResourcePermission,
                ResourcePermissionClaimAuthorization.CreateResourcePermissionClaimValue(
                    grant.TargetResourceId,
                    grant.Permission)));
        }

        return claims.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyList<Claim>)entry.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    private bool MatchesInMemoryProvider(string? providerId) =>
        string.IsNullOrWhiteSpace(providerId) ||
        string.Equals(providerId, inMemoryIdentity.ProviderId, StringComparison.OrdinalIgnoreCase);

    private static bool SameClaim(Claim left, Claim right) =>
        string.Equals(left.Type, right.Type, StringComparison.Ordinal) &&
        string.Equals(left.Value, right.Value, StringComparison.Ordinal);

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
