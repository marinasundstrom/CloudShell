using CloudShell.Abstractions.Authentication;
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
    IOptions<CloudShellAuthenticationOptions> options) : IAccountService
{
    public string Mode => options.Value.Mode;

    public bool AllowLocalSetup => options.Value.AllowLocalSetup;

    public async Task<bool> HasLocalUsersAsync(CancellationToken cancellationToken = default)
    {
        if (!IsMode("Identity"))
        {
            return false;
        }

        var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
        return await userManager.Users.AnyAsync(cancellationToken);
    }

    public async Task<AccountOperationResult> SignInAsync(
        string userName,
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

        var signInManager = services.GetRequiredService<SignInManager<IdentityUser>>();
        var result = await signInManager.PasswordSignInAsync(
            userName,
            credential,
            isPersistent: true,
            lockoutOnFailure: true);

        return result.Succeeded
            ? AccountOperationResult.Success()
            : AccountOperationResult.Failure("The username or password is invalid.");
    }

    public async Task<AccountOperationResult> CreateAdministratorAsync(
        string email,
        string password)
    {
        if (!IsMode("Identity") || !AllowLocalSetup)
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
}
