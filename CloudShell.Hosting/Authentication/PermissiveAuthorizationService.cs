using CloudShell.Abstractions.Authorization;

namespace CloudShell.Hosting.Authentication;

internal sealed class PermissiveAuthorizationService : ICloudShellAuthorizationService
{
    public bool IsAuthenticated => true;

    public bool HasPermission(string permission) => true;

    public bool CanAccessResourceGroup(string? resourceGroupId, string permission) => true;

    public bool CanAccessResource(string resourceId, string? resourceGroupId, string permission) => true;
}
