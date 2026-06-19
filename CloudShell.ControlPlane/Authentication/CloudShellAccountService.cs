using CloudShell.Abstractions.Authentication;
using CloudShell.Abstractions.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CloudShell.ControlPlane.Authentication;

public sealed class CloudShellAccountService(
    IServiceProvider services,
    IHttpContextAccessor httpContextAccessor,
    CloudShellSecretSignInService secretSignIn,
    IOptions<CloudShellAuthenticationOptions> options,
    InMemoryIdentitySetupOptions inMemoryIdentity) : IAccountService
{
    public string Mode => options.Value.Mode;

    public bool AllowLocalSetup => options.Value.AllowLocalSetup;

    public bool SupportsLocalUserAdministration => UsesLocalIdentity;

    public CloudShellLocalUserStoreKind LocalUserStoreKind =>
        !SupportsLocalUserAdministration
            ? CloudShellLocalUserStoreKind.Unavailable
            : inMemoryIdentity.IsConfigured && inMemoryIdentity.UseAspNetCoreIdentityStore
                ? CloudShellLocalUserStoreKind.InMemory
                : CloudShellLocalUserStoreKind.Persistent;

    public bool AllowUserNameSignIn => options.Value.BuiltInIdentity.AllowUserNameSignIn;

    public async Task<bool> HasLocalUsersAsync(CancellationToken cancellationToken = default)
    {
        if (!UsesLocalIdentity)
        {
            return false;
        }

        var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
        cancellationToken.ThrowIfCancellationRequested();
        return userManager.Users.Any();
    }

    public async Task<AccountOperationResult> SignInAsync(
        string identifier,
        string credential)
    {
        if (IsMode("Secret"))
        {
            return await secretSignIn.SignInAsync(credential)
                ? AccountOperationResult.Success()
                : AccountOperationResult.Failure("The dashboard secret is invalid.");
        }

        if (!IsMode("Identity"))
        {
            return AccountOperationResult.Failure(
                "This authentication provider uses its external sign-in flow.");
        }

        var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
        var user = await BuiltInIdentityUserLookup.FindBySignInIdentifierAsync(
            userManager,
            options.Value,
            identifier);
        if (user is null)
        {
            return AccountOperationResult.Failure(
                BuiltInIdentityUserLookup.InvalidCredentialsMessage(options.Value));
        }

        var signInManager = services.GetRequiredService<SignInManager<IdentityUser>>();
        var result = await signInManager.PasswordSignInAsync(
            user,
            credential,
            isPersistent: true,
            lockoutOnFailure: true);

        return result.Succeeded
            ? AccountOperationResult.Success()
            : AccountOperationResult.Failure(
                BuiltInIdentityUserLookup.InvalidCredentialsMessage(options.Value));
    }

    public async Task<AccountOperationResult> CreateAdministratorAsync(
        string email,
        string password)
    {
        if (!UsesLocalIdentity || !AllowLocalSetup)
        {
            return AccountOperationResult.Failure("Local account setup is disabled.");
        }

        var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
        if (await userManager.Users.AnyAsync())
        {
            return AccountOperationResult.Failure(
                "CloudShell already has a local administrator.");
        }

        var user = new IdentityUser
        {
            UserName = email.Trim(),
            Email = email.Trim(),
            EmailConfirmed = true
        };
        var createResult = await userManager.CreateAsync(user, password);
        if (!createResult.Succeeded)
        {
            return AccountOperationResult.Failure(
                createResult.Errors.Select(error => error.Description).ToArray());
        }

        var roleName = options.Value.AdministratorRole;
        if (!options.Value.RolePermissions.ContainsKey(roleName))
        {
            await userManager.DeleteAsync(user);
            return AccountOperationResult.Failure(
                $"The configured administrator role '{roleName}' is not defined.");
        }

        var roleResult = await userManager.AddToRoleAsync(user, roleName);
        if (!roleResult.Succeeded)
        {
            await userManager.DeleteAsync(user);
            return AccountOperationResult.Failure(
                roleResult.Errors.Select(error => error.Description).ToArray());
        }

        var signInManager = services.GetRequiredService<SignInManager<IdentityUser>>();
        await signInManager.SignInAsync(user, isPersistent: true);
        return AccountOperationResult.Success();
    }

    public async Task<IReadOnlyList<CloudShellAccountUser>> ListUsersAsync(
        CancellationToken cancellationToken = default)
    {
        if (!UsesLocalIdentity)
        {
            return [];
        }

        var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
        cancellationToken.ThrowIfCancellationRequested();
        var users = userManager.Users
            .OrderBy(user => user.Email ?? user.UserName)
            .ToList();
        var result = new List<CloudShellAccountUser>(users.Count);
        foreach (var user in users)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var roles = await userManager.GetRolesAsync(user);
            var claims = await userManager.GetClaimsAsync(user);
            result.Add(new CloudShellAccountUser(
                user.Id,
                user.UserName ?? user.Email ?? user.Id,
                user.Email,
                roles.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                claims
                    .Where(IsCloudShellClaim)
                    .OrderBy(claim => claim.Type, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(claim => claim.Value, StringComparer.OrdinalIgnoreCase)
                    .Select(claim => new CloudShellAccountClaim(claim.Type, claim.Value))
                    .ToArray()));
        }

        return result;
    }

    public async Task<AccountOperationResult> CreateUserAsync(
        CreateCloudShellAccountUserRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!UsesLocalIdentity)
        {
            return AccountOperationResult.Failure("Local user administration is unavailable.");
        }

        var email = request.Email.Trim();
        if (string.IsNullOrWhiteSpace(email))
        {
            return AccountOperationResult.Failure("Email is required.");
        }

        var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
        var user = new IdentityUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true
        };
        var createResult = await userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            return AccountOperationResult.Failure(
                createResult.Errors.Select(error => error.Description).ToArray());
        }

        var errors = new List<string>();
        var roleName = request.Role?.Trim();
        if (!string.IsNullOrWhiteSpace(roleName))
        {
            if (!options.Value.RolePermissions.ContainsKey(roleName))
            {
                await userManager.DeleteAsync(user);
                return AccountOperationResult.Failure($"The configured role '{roleName}' is not defined.");
            }

            var roleResult = await userManager.AddToRoleAsync(user, roleName);
            if (!roleResult.Succeeded)
            {
                await userManager.DeleteAsync(user);
                return AccountOperationResult.Failure(
                    roleResult.Errors.Select(error => error.Description).ToArray());
            }
        }

        foreach (var accountClaim in NormalizeClaims(request.Claims ?? []))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var claimResult = await userManager.AddClaimAsync(
                user,
                new System.Security.Claims.Claim(accountClaim.Type, accountClaim.Value));
            if (!claimResult.Succeeded)
            {
                errors.AddRange(claimResult.Errors.Select(error => error.Description));
            }
        }

        if (errors.Count > 0)
        {
            await userManager.DeleteAsync(user);
            return AccountOperationResult.Failure(errors.ToArray());
        }

        return AccountOperationResult.Success();
    }

    public async Task SignOutAsync()
    {
        var context = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("An HTTP context is required to sign out.");

        if (IsMode("Identity"))
        {
            await services.GetRequiredService<SignInManager<IdentityUser>>().SignOutAsync();
            return;
        }

        await context.SignOutAsync(options.Value.DefaultScheme);
    }

    private bool IsMode(string mode) =>
        string.Equals(Mode, mode, StringComparison.OrdinalIgnoreCase);

    private bool UsesLocalIdentity =>
        options.Value.Enabled &&
        IsMode("Identity");

    private static IReadOnlyList<CloudShellAccountClaim> NormalizeClaims(
        IReadOnlyList<CloudShellAccountClaim> claims) =>
        claims
            .Select(claim => new CloudShellAccountClaim(claim.Type.Trim(), claim.Value.Trim()))
            .Where(claim =>
                IsCloudShellClaim(claim) &&
                !string.IsNullOrWhiteSpace(claim.Value))
            .Distinct()
            .ToArray();

    private static bool IsCloudShellClaim(CloudShellAccountClaim claim) =>
        claim.Type is CloudShellAuthorizationClaimTypes.Permission or
            CloudShellAuthorizationClaimTypes.ResourceGroup or
            CloudShellAuthorizationClaimTypes.Resource or
            CloudShellAuthorizationClaimTypes.ResourcePermission;

    private static bool IsCloudShellClaim(System.Security.Claims.Claim claim) =>
        claim.Type is CloudShellAuthorizationClaimTypes.Permission or
            CloudShellAuthorizationClaimTypes.ResourceGroup or
            CloudShellAuthorizationClaimTypes.Resource or
            CloudShellAuthorizationClaimTypes.ResourcePermission;
}
