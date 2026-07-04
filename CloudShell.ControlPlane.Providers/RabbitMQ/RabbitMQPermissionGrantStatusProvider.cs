using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.Providers;

public sealed class RabbitMQPermissionGrantStatusProvider(
    IEnumerable<IRabbitMQPermissionGrantEffectivenessProvider>? effectivenessProviders = null) :
    IResourcePermissionGrantStatusProvider
{
    private readonly IReadOnlyList<IRabbitMQPermissionGrantEffectivenessProvider> _effectivenessProviders =
        effectivenessProviders?.ToArray() ?? [];

    public string ProviderId => RabbitMQResourceTypeProvider.ProviderId;

    public bool CanGetStatus(ResourcePermissionGrantStatusRequest request) =>
        string.Equals(
            request.TargetResource.EffectiveTypeId,
            RabbitMQResourceTypeProvider.ResourceTypeId.ToString(),
            StringComparison.OrdinalIgnoreCase) &&
        IsRabbitMQPermission(request.Grant.Permission);

    public async Task<ResourcePermissionGrantStatus> GetStatusAsync(
        ResourcePermissionGrantStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(
                request.Grant.Permission,
                RabbitMQResourceOperationPermissions.ReconcileAccess,
                StringComparison.OrdinalIgnoreCase))
        {
            return new ResourcePermissionGrantStatus(
                request.Grant,
                ResourcePermissionGrantEffectivenessState.Applied,
                "RabbitMQ reconcile access is a CloudShell operation grant and is enforced by CloudShell authorization.",
                ProviderId,
                DateTimeOffset.UtcNow);
        }

        foreach (var provider in _effectivenessProviders)
        {
            if (provider.CanGetStatus(request))
            {
                return await provider.GetStatusAsync(request, cancellationToken);
            }
        }

        return new ResourcePermissionGrantStatus(
            request.Grant,
            ResourcePermissionGrantEffectivenessState.Pending,
            "RabbitMQ grants are recorded in CloudShell. Broker-native user and permission reconciliation has not reported effective state yet.",
            ProviderId,
            DateTimeOffset.UtcNow);
    }

    internal static bool IsRabbitMQPermission(string permission) =>
        string.Equals(permission, RabbitMQResourceOperationPermissions.Publish, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(permission, RabbitMQResourceOperationPermissions.Consume, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(permission, RabbitMQResourceOperationPermissions.Configure, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(permission, RabbitMQResourceOperationPermissions.ReconcileAccess, StringComparison.OrdinalIgnoreCase);
}
