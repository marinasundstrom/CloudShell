namespace CloudShell.Abstractions.Authorization;

public interface ICloudShellAuthorizationService
{
    bool IsAuthenticated { get; }

    bool HasPermission(string permission);

    bool CanAccessResourceGroup(string? resourceGroupId, string permission);

    bool CanAccessResource(string resourceId, string? resourceGroupId, string permission);
}
