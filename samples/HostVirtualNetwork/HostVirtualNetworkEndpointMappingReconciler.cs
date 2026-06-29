using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceManager.Networking;
using CloudShell.ResourceModel;
using CloudShell.ControlPlane.Providers;
using CloudShell.ControlPlane.ResourceModel;
using ResourceModelResource = CloudShell.ResourceModel.Resource;
using ResourceManagerClass = CloudShell.Abstractions.ResourceManager.ResourceClass;
using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;

namespace CloudShell.HostVirtualNetwork;

public sealed class HostVirtualNetworkEndpointMappingReconciler(
    IEnumerable<IResourceEndpointMappingProvisioner> endpointMappingProvisioners,
    IEnumerable<IResourceModelResourceManagerEndpointProjectionProvider> endpointProjectionProviders) :
    IVirtualNetworkEndpointMappingReconciler
{
    private readonly IReadOnlyList<IResourceEndpointMappingProvisioner> _endpointMappingProvisioners =
        endpointMappingProvisioners.ToArray();
    private readonly IReadOnlyList<IResourceModelResourceManagerEndpointProjectionProvider> _endpointProjectionProviders =
        endpointProjectionProviders.ToArray();

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileEndpointMappingsAsync(
        ResourceModelResource resource,
        ResourceProjectionExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(context);

        var resourceManagerResources = context.Resources
            .Select(ToResourceManagerResource)
            .ToArray();
        var resourceManager = new HostVirtualNetworkResourceManagerStore(resourceManagerResources);
        var networkResource = resourceManager.GetResource(resource.EffectiveResourceId);
        if (networkResource is null)
        {
            return
            [
                ResourceDefinitionDiagnostic.Error(
                    "hostVirtualNetwork.endpointMappingNetworkMissing",
                    $"Virtual network resource '{resource.EffectiveResourceId}' could not be projected for endpoint-mapping reconciliation.",
                    resource.EffectiveResourceId)
            ];
        }

        if (networkResource.ResourceEndpointMappings.Count == 0)
        {
            return
            [
                new ResourceDefinitionDiagnostic(
                    ResourceDefinitionDiagnosticSeverity.Information,
                    "hostVirtualNetwork.endpointMappingsEmpty",
                    "No endpoint mappings to reconcile.",
                    resource.EffectiveResourceId)
            ];
        }

        var diagnostics = new List<ResourceDefinitionDiagnostic>();
        var provisionedCount = 0;
        foreach (var mapping in networkResource.ResourceEndpointMappings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (await TryProvisionEndpointMappingAsync(
                        networkResource,
                        mapping,
                        resourceManager,
                        cancellationToken))
                {
                    provisionedCount++;
                }
            }
            catch (InvalidOperationException exception)
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    "hostVirtualNetwork.endpointMappingProvisioningFailed",
                    exception.Message,
                    mapping.Id));
            }
        }

        if (diagnostics.Count == 0)
        {
            diagnostics.Add(new ResourceDefinitionDiagnostic(
                ResourceDefinitionDiagnosticSeverity.Information,
                "hostVirtualNetwork.endpointMappingsReconciled",
                $"Reconciled {networkResource.ResourceEndpointMappings.Count} endpoint mapping(s), provisioned {provisionedCount}.",
                resource.EffectiveResourceId));
        }

        return diagnostics;
    }

    private async Task<bool> TryProvisionEndpointMappingAsync(
        ResourceManagerResource networkResource,
        ResourceEndpointMappingDefinition mapping,
        IResourceManagerStore resourceManager,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                mapping.Source.ResourceId,
                networkResource.Id,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Endpoint mapping '{mapping.Id}' source resource '{mapping.Source.ResourceId}' must be the virtual network resource '{networkResource.Id}'.");
        }

        var source = ResolveEndpointReference(resourceManager, mapping, mapping.Source, "source");
        var target = ResolveEndpointReference(resourceManager, mapping, mapping.Target, "target");
        var provider = AdaptProviderResource(ValidateMappingProvider(resourceManager, mapping));
        if (!RequiresEndpointMappingProvisioner(networkResource, provider))
        {
            return false;
        }

        var networkDefinition = new NetworkResourceDefinition(
            networkResource.Id,
            networkResource.Name,
            IsDefault: IsDefaultVirtualNetwork(networkResource),
            EndpointMappings: networkResource.ResourceEndpointMappings,
            Kind: NetworkResourceKind.Virtual);
        var provisioningContext = new ResourceEndpointMappingProvisioningContext(
            networkResource,
            networkDefinition,
            mapping,
            source.Endpoint,
            target.Resource,
            target.Endpoint,
            provider,
            resourceManager,
            source.EndpointNetworkMapping,
            target.EndpointNetworkMapping);
        var provisioner = _endpointMappingProvisioners.FirstOrDefault(candidate =>
            candidate.CanProvisionEndpointMapping(provisioningContext));
        if (provisioner is null)
        {
            throw new InvalidOperationException(
                $"Endpoint mapping '{mapping.Id}' requires provider resource '{provider.Id}', but no host networking provisioner can materialize it.");
        }

        await provisioner.ProvisionEndpointMappingAsync(provisioningContext, cancellationToken);
        return true;
    }

    private ResourceManagerResource ToResourceManagerResource(
        ResourceModelResource resource) =>
        ResourceModelResourceManagerMapper.ToResourceManagerResource(
            resource,
            new ResourceModelResourceManagerProjectionOptions(
                EndpointProjectionResolver: ResolveEndpointProjection));

    private ResourceModelResourceManagerEndpointProjection? ResolveEndpointProjection(
        ResourceModelResource resource)
    {
        foreach (var provider in _endpointProjectionProviders)
        {
            var projection = provider.GetEndpointProjection(resource);
            if (projection is not null)
            {
                return projection;
            }
        }

        return null;
    }

    private static bool RequiresEndpointMappingProvisioner(
        ResourceManagerResource networkResource,
        ResourceManagerResource provider) =>
        !string.Equals(provider.Id, networkResource.Id, StringComparison.OrdinalIgnoreCase);

    private static bool IsDefaultVirtualNetwork(ResourceManagerResource networkResource) =>
        networkResource.ResourceAttributes.TryGetValue(
            VirtualNetworkResourceTypeProvider.Attributes.IsDefault.ToString(),
            out var value) &&
        bool.TryParse(value, out var isDefault) &&
        isDefault;

    private static ResolvedEndpoint ResolveEndpointReference(
        IResourceManagerStore resourceManager,
        ResourceEndpointMappingDefinition mapping,
        ResourceEndpointReference endpoint,
        string role)
    {
        var resource = resourceManager.GetResource(endpoint.ResourceId)
            ?? throw new InvalidOperationException(
                $"Endpoint mapping '{mapping.Id}' {role} resource '{endpoint.ResourceId}' could not be found.");
        var resolvedEndpoint = resource.Endpoints.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, endpoint.EndpointName, StringComparison.OrdinalIgnoreCase));
        if (resolvedEndpoint is null)
        {
            throw new InvalidOperationException(
                $"Endpoint mapping '{mapping.Id}' {role} endpoint '{endpoint.EndpointName}' could not be found on resource '{endpoint.ResourceId}'.");
        }

        return new ResolvedEndpoint(
            resource,
            resolvedEndpoint,
            resource.GetEndpointNetworkMapping(resolvedEndpoint.Name));
    }

    private static ResourceManagerResource ValidateMappingProvider(
        IResourceManagerStore resourceManager,
        ResourceEndpointMappingDefinition mapping)
    {
        var providerResourceId = FirstNonEmpty(
                mapping.ProviderResourceId,
                mapping.NetworkResourceId,
                mapping.Source.ResourceId)
            ?? throw new InvalidOperationException(
                $"Endpoint mapping '{mapping.Id}' does not specify a provider resource.");
        var provider = resourceManager.GetResource(providerResourceId)
            ?? throw new InvalidOperationException(
                $"Endpoint mapping '{mapping.Id}' provider resource '{providerResourceId}' could not be found.");
        if (!provider.HasCapability(ResourceCapabilityIds.NetworkingEndpointMapper))
        {
            throw new InvalidOperationException(
                $"Endpoint mapping '{mapping.Id}' provider resource '{providerResourceId}' does not advertise '{ResourceCapabilityIds.NetworkingEndpointMapper}'.");
        }

        return provider;
    }

    private static ResourceManagerResource AdaptProviderResource(
        ResourceManagerResource provider) =>
        string.Equals(
            provider.EffectiveTypeId,
            LocalHostNetworkResourceTypeProvider.ResourceTypeId.ToString(),
            StringComparison.OrdinalIgnoreCase)
            ? provider with
            {
                Id = LocalHostNetworkProvider.ResourceId,
                TypeId = LocalHostNetworkProvider.ResourceType
            }
            : provider;

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private sealed record ResolvedEndpoint(
        ResourceManagerResource Resource,
        ResourceEndpoint Endpoint,
        ResourceEndpointNetworkMapping? EndpointNetworkMapping);

    private sealed class HostVirtualNetworkResourceManagerStore(
        IReadOnlyList<ResourceManagerResource> resources) : IResourceManagerStore
    {
        public IReadOnlyList<IResourceProvider> Providers => [];

        public IReadOnlyList<ResourceGroup> GetResourceGroups() => [];

        public IReadOnlyList<ResourceManagerResource> GetAvailableResources() => resources;

        public IReadOnlyList<ResourceManagerResource> GetResources() => resources;

        public IReadOnlyList<ResourceModelDiagnostic> GetResourceModelDiagnostics() => [];

        public ResourceManagerClass? GetResourceTypeClass(string resourceType) =>
            resources
                .FirstOrDefault(resource =>
                    string.Equals(resource.EffectiveTypeId, resourceType, StringComparison.OrdinalIgnoreCase))
                ?.ResourceClass;

        public ResourceManagerResource? GetResource(string id) =>
            resources.FirstOrDefault(resource =>
                string.Equals(resource.Id, id, StringComparison.OrdinalIgnoreCase));

        public IReadOnlyList<ResourceManagerResource> GetChildren(string resourceId) => [];

        public ResourceGroup? GetGroupForResource(string resourceId) => null;

        public bool IsRegistered(string resourceId) =>
            GetResource(resourceId) is not null;
    }
}
