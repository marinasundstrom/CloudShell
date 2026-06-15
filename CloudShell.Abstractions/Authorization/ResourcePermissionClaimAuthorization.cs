using System.Security.Claims;
using System.Text.Json;

namespace CloudShell.Abstractions.Authorization;

/// <summary>
/// Evaluates CloudShell resource-permission claims for protected resource services.
/// </summary>
/// <remarks>
/// Preview API: the platform owns this integration contract, but the protected-service
/// authentication surface may still evolve before the MVP API is declared stable.
/// </remarks>
public static class ResourcePermissionClaimAuthorization
{
    public static bool HasResourcePermission(
        ClaimsPrincipal? user,
        string resourceId,
        string permission) =>
        user?.Identity?.IsAuthenticated == true &&
        user.Claims
            .SelectMany(GetResourcePermissionClaimValues)
            .Any(value => ResourcePermissionClaimMatches(value, resourceId, permission));

    public static string CreateResourcePermissionClaimValue(
        string resourceId,
        string permission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);

        return string.Join(
            CloudShellAuthorizationClaimTypes.ResourcePermissionSeparator,
            resourceId.Trim(),
            permission.Trim());
    }

    private static IEnumerable<string> GetResourcePermissionClaimValues(Claim claim)
    {
        if (string.Equals(
                claim.Type,
                CloudShellAuthorizationClaimTypes.ResourcePermission,
                StringComparison.OrdinalIgnoreCase))
        {
            yield return claim.Value;
            yield break;
        }

        if (!string.Equals(claim.Type, "cloudshell", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(claim.Value))
        {
            yield break;
        }

        using var document = TryParseJson(claim.Value);
        if (document is null ||
            !document.RootElement.TryGetProperty("resource-permission", out var resourcePermission))
        {
            yield break;
        }

        if (resourcePermission.ValueKind == JsonValueKind.String)
        {
            yield return resourcePermission.GetString() ?? string.Empty;
            yield break;
        }

        if (resourcePermission.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in resourcePermission.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                yield return item.GetString() ?? string.Empty;
            }
        }
    }

    private static JsonDocument? TryParseJson(string value)
    {
        try
        {
            return JsonDocument.Parse(value);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool ResourcePermissionClaimMatches(
        string value,
        string resourceId,
        string permission)
    {
        var separatorIndex = value.IndexOf(CloudShellAuthorizationClaimTypes.ResourcePermissionSeparator);
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
