using CloudShell.Abstractions.ResourceManager;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace CloudShell.Providers.Applications;

internal static class SqlServerCredentialNames
{
    private const string ManagedUserPrefix = "cloudshell_";

    public static string CreateManagedUserName(ResourcePermissionGrant grant)
    {
        var identity = grant.ResourceIdentity
            ?? throw new InvalidOperationException("SQL Server managed user names require a resource identity grant.");
        var key = $"{identity.ResourceId}\u001f{identity.Name}\u001f{grant.TargetResourceId}\u001f{grant.Permission}";
        return CreateManagedUserName(key);
    }

    public static string CreateManagedUserNameFromPrincipalSubject(
        string subject,
        string targetResourceId,
        string permission)
    {
        var key = TryCreateBuiltInResourceIdentityGrantKey(
            subject,
            targetResourceId,
            permission) ?? $"{subject}\u001f{targetResourceId}\u001f{permission}";
        return CreateManagedUserName(key);
    }

    public static string CreateCredentialPassword()
    {
        Span<byte> bytes = stackalloc byte[24];
        RandomNumberGenerator.Fill(bytes);
        return $"Cs1!{Convert.ToBase64String(bytes).Replace('+', 'A').Replace('/', 'b')}";
    }

    public static string GetPrincipalSubject(ClaimsPrincipal principal)
    {
        var subject =
            principal.FindFirstValue(ClaimTypes.NameIdentifier) ??
            principal.FindFirstValue("sub") ??
            principal.Identity?.Name;

        return string.IsNullOrWhiteSpace(subject)
            ? throw new UnauthorizedAccessException("The CloudShell resource identity token does not include a subject.")
            : subject;
    }

    private static string? TryCreateBuiltInResourceIdentityGrantKey(
        string subject,
        string targetResourceId,
        string permission)
    {
        var separatorIndex = subject.IndexOf('/');
        if (separatorIndex <= 0 || separatorIndex == subject.Length - 1)
        {
            return null;
        }

        var resourceId = subject[..separatorIndex];
        var identityName = subject[(separatorIndex + 1)..];
        return $"{resourceId}\u001f{identityName}\u001f{targetResourceId}\u001f{permission}";
    }

    private static string CreateManagedUserName(string key)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(key), hash);
        return $"{ManagedUserPrefix}{Convert.ToHexString(hash[..10]).ToLowerInvariant()}";
    }
}
