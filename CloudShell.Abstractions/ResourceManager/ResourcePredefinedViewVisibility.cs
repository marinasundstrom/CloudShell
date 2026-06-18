namespace CloudShell.Abstractions.ResourceManager;

public static class ResourcePredefinedViewVisibility
{
    public static bool HasEndpointsView(Resource? resource) =>
        resource is not null &&
        (resource.Endpoints.Count > 0 ||
         resource.ResourceEndpointMappings.Count > 0 ||
         resource.ResourceLoadBalancerRoutes.Count > 0 ||
         resource.ResourceClass == ResourceClass.Network ||
         resource.HasCapability(ResourceCapabilityIds.EndpointSource) ||
         resource.ResourceCapabilities.Any(capability =>
             capability.Id.StartsWith("networking.", StringComparison.OrdinalIgnoreCase)));

    public static bool HasDnsView(Resource? resource) =>
        resource is not null &&
        (resource.Endpoints.Count > 0 ||
         resource.HasCapability(ResourceCapabilityIds.NetworkingDnsZone) ||
         resource.HasCapability(ResourceCapabilityIds.NetworkingNameMapping) ||
         resource.HasCapability(ResourceCapabilityIds.NetworkingNamePublisher) ||
         resource.HasCapability(ResourceCapabilityIds.NetworkingNameResolver) ||
         string.Equals(resource.EffectiveTypeId, "cloudshell.dnsZone", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(resource.EffectiveTypeId, "cloudshell.nameMapping", StringComparison.OrdinalIgnoreCase));

    public static bool HasIdentityView(Resource? resource, bool hasDefaultIdentityProvider = false) =>
        resource is not null &&
        hasDefaultIdentityProvider;

    public static bool HasAccessControlView(Resource? resource, bool hasDefaultIdentityProvider = false) =>
        resource is not null &&
        hasDefaultIdentityProvider;

    public static bool HasStorageVolumesView(Resource? resource) =>
        resource?.HasCapability(ResourceCapabilityIds.StorageProvider) == true;

    public static bool HasEnvironmentView(Resource? resource) =>
        resource?.HasCapability(ResourceCapabilityIds.EnvironmentVariables) == true;
}
