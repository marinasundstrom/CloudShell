namespace CloudShell.Abstractions.Authorization;

public static class UsageAuthorization
{
    public static IReadOnlyList<string> UsageReadPermissions { get; } =
    [
        CloudShellPermissions.Usage.Read
    ];

    public static bool CanReadUsage(this ICloudShellAuthorizationService authorization) =>
        HasAnyPermission(authorization, UsageReadPermissions);

    public static bool HasAnyPermission(
        ICloudShellAuthorizationService authorization,
        IEnumerable<string> permissions)
    {
        foreach (var permission in permissions)
        {
            if (authorization.HasPermission(permission))
            {
                return true;
            }
        }

        return false;
    }
}
