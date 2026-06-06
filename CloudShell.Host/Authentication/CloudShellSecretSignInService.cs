using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CloudShell.Abstractions.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace CloudShell.Host.Authentication;

public sealed class CloudShellSecretSignInService(
    IHttpContextAccessor httpContextAccessor,
    IOptions<CloudShellAuthenticationOptions> options)
{
    public async Task<bool> SignInAsync(string secret)
    {
        var expected = options.Value.Secret;
        if (string.IsNullOrEmpty(expected) || !SecretsMatch(expected, secret))
        {
            return false;
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, "CloudShell administrator"),
            new Claim(
                CloudShellAuthenticationOptions.PermissionClaimType,
                CloudShellPermissions.All),
            new Claim(
                CloudShellAuthenticationOptions.ResourceGroupClaimType,
                CloudShellPermissions.All),
            new Claim(
                CloudShellAuthenticationOptions.ResourceClaimType,
                CloudShellPermissions.All)
        };
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(claims, options.Value.DefaultScheme));
        var context = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("An HTTP context is required to sign in.");

        await context.SignInAsync(options.Value.DefaultScheme, principal);
        return true;
    }

    private static bool SecretsMatch(string expected, string actual)
    {
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var actualHash = SHA256.HashData(Encoding.UTF8.GetBytes(actual));
        return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
    }
}
