namespace CloudShell.Abstractions.Authorization;

public enum ResourceAccessLevel
{
    None = 0,
    Reference = 1,
    Read = 2,
    Operate = 3,
    Manage = 4
}

public static class ResourceAccessLevelExtensions
{
    public static ResourceAccessLevel GetResourceAccessLevel(
        this ICloudShellAuthorizationService authorization,
        string resourceId,
        string? resourceGroupId,
        IEnumerable<string>? operationPermissions = null)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        if (authorization.CanAccessResource(
                resourceId,
                resourceGroupId,
                CloudShellPermissions.Resources.Manage))
        {
            return ResourceAccessLevel.Manage;
        }

        if (operationPermissions is not null &&
            operationPermissions.Any(permission =>
                !string.IsNullOrWhiteSpace(permission) &&
                authorization.CanAccessResource(resourceId, resourceGroupId, permission)))
        {
            return ResourceAccessLevel.Operate;
        }

        if (authorization.CanAccessResource(
                resourceId,
                resourceGroupId,
                CloudShellPermissions.Resources.Read))
        {
            return ResourceAccessLevel.Read;
        }

        if (authorization.CanAccessResource(
                resourceId,
                resourceGroupId,
                CloudShellPermissions.Resources.Reference))
        {
            return ResourceAccessLevel.Reference;
        }

        return ResourceAccessLevel.None;
    }

    public static bool AllowsReference(this ResourceAccessLevel accessLevel) =>
        accessLevel >= ResourceAccessLevel.Reference;

    public static bool AllowsRead(this ResourceAccessLevel accessLevel) =>
        accessLevel >= ResourceAccessLevel.Read;

    public static bool AllowsOperate(this ResourceAccessLevel accessLevel) =>
        accessLevel >= ResourceAccessLevel.Operate;

    public static bool AllowsManage(this ResourceAccessLevel accessLevel) =>
        accessLevel >= ResourceAccessLevel.Manage;
}

public sealed record ResourceAccessPermissionSet(
    ResourceAccessLevel AccessLevel,
    IReadOnlyList<string> Permissions)
{
    public IReadOnlyList<string> Permissions { get; init; } =
        NormalizePermissions(Permissions);

    private static IReadOnlyList<string> NormalizePermissions(IReadOnlyList<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        return permissions
            .Where(permission => !string.IsNullOrWhiteSpace(permission))
            .Select(permission => permission.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

public static class ResourceAccessPermissions
{
    public static ResourceAccessPermissionSet Reference { get; } = new(
        ResourceAccessLevel.Reference,
        [CloudShellPermissions.Resources.Reference]);

    public static ResourceAccessPermissionSet Read { get; } = new(
        ResourceAccessLevel.Read,
        [CloudShellPermissions.Resources.Read]);

    public static ResourceAccessPermissionSet Manage { get; } = new(
        ResourceAccessLevel.Manage,
        [CloudShellPermissions.Resources.Manage]);

    public static ResourceAccessPermissionSet Operate(params string[] permissions) =>
        Operate((IReadOnlyList<string>)permissions);

    public static ResourceAccessPermissionSet Operate(IReadOnlyList<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        if (permissions.Count == 0)
        {
            throw new ArgumentException("At least one operation permission is required.", nameof(permissions));
        }

        return new ResourceAccessPermissionSet(ResourceAccessLevel.Operate, permissions);
    }
}
