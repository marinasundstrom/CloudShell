using Microsoft.AspNetCore.Identity;

namespace CloudShell.ControlPlane.Authentication;

internal static class BuiltInIdentityUserLookup
{
    public static async Task<IdentityUser?> FindBySignInIdentifierAsync(
        UserManager<IdentityUser> userManager,
        CloudShellAuthenticationOptions options,
        string identifier)
    {
        var normalizedIdentifier = identifier.Trim();
        var user = await userManager.FindByEmailAsync(normalizedIdentifier);
        if (user is not null ||
            !options.BuiltInIdentity.AllowUserNameSignIn)
        {
            return user;
        }

        return await userManager.FindByNameAsync(normalizedIdentifier);
    }

    public static string RequiredIdentifierMessage(CloudShellAuthenticationOptions options) =>
        options.BuiltInIdentity.AllowUserNameSignIn
            ? "Email or username is required."
            : "Email is required.";

    public static string InvalidCredentialsMessage(CloudShellAuthenticationOptions options) =>
        options.BuiltInIdentity.AllowUserNameSignIn
            ? "The email/username or password is invalid."
            : "The email or password is invalid.";

    public static string InvalidGrantRequiredDescription(CloudShellAuthenticationOptions options) =>
        options.BuiltInIdentity.AllowUserNameSignIn
            ? "email or username and password are required."
            : "email and password are required.";
}
