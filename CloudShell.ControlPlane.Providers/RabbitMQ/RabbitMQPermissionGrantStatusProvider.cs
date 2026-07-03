using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.Providers;

public sealed class RabbitMQPermissionGrantStatusProvider :
    IResourcePermissionGrantStatusProvider
{
    public string ProviderId => RabbitMQResourceTypeProvider.ProviderId;

    public bool CanGetStatus(ResourcePermissionGrantStatusRequest request) =>
        string.Equals(
            request.TargetResource.EffectiveTypeId,
            RabbitMQResourceTypeProvider.ResourceTypeId.ToString(),
            StringComparison.OrdinalIgnoreCase) &&
        IsRabbitMQPermission(request.Grant.Permission);

    public Task<ResourcePermissionGrantStatus> GetStatusAsync(
        ResourcePermissionGrantStatusRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ResourcePermissionGrantStatus(
            request.Grant,
            ResourcePermissionGrantEffectivenessState.Pending,
            "RabbitMQ grants are recorded in CloudShell. Broker-native user and permission reconciliation has not reported effective state yet.",
            ProviderId,
            DateTimeOffset.UtcNow));

    internal static bool IsRabbitMQPermission(string permission) =>
        string.Equals(permission, RabbitMQResourceOperationPermissions.Publish, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(permission, RabbitMQResourceOperationPermissions.Consume, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(permission, RabbitMQResourceOperationPermissions.Configure, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(permission, RabbitMQResourceOperationPermissions.ReconcileAccess, StringComparison.OrdinalIgnoreCase);
}
