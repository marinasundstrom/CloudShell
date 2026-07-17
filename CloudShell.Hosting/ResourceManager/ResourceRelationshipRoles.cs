using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Hosting.ResourceManager;

internal static class ResourceRelationshipRoles
{
    public static string GetRole(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        if (resource.HasCapability(ResourceCapabilityIds.NetworkingLoadBalancer) ||
            resource.ResourceLoadBalancerRoutes.Count > 0 ||
            resource.ResourceAttributes.ContainsKey(ResourceAttributeNames.LoadBalancerProvider))
        {
            return "Exposure resource";
        }

        if (resource.HasCapability(ResourceCapabilityIds.NetworkingDnsZone) ||
            resource.HasCapability(ResourceCapabilityIds.NetworkingNameMapping))
        {
            return "Name resolution resource";
        }

        if (resource.HasCapability(ResourceCapabilityIds.ContainerHost))
        {
            return "Container host";
        }

        if (resource.ResourceAttributes.TryGetValue(ResourceAttributeNames.InfrastructureKind, out var infrastructureKind) &&
            string.Equals(infrastructureKind, "identity-provisioning", StringComparison.OrdinalIgnoreCase))
        {
            return "Identity provider boundary";
        }

        if (resource.HasCapability(ResourceCapabilityIds.StorageVolume))
        {
            return "Storage volume";
        }

        if (resource.HasCapability(ResourceCapabilityIds.StorageProvider))
        {
            return "Storage provider";
        }

        return resource.ResourceClass switch
        {
            ResourceClass.Executable or ResourceClass.Project or ResourceClass.Container => "Application workload",
            ResourceClass.Service => "Service resource",
            ResourceClass.Network => "Network resource",
            ResourceClass.Storage => "Storage resource",
            ResourceClass.Configuration => "Configuration service",
            ResourceClass.Infrastructure => "Infrastructure resource",
            ResourceClass.SecretsVault => "Secrets vault",
            _ => "Registered resource"
        };
    }
}
