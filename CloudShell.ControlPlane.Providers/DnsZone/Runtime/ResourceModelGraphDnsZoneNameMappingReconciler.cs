using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceModel;
using Microsoft.Extensions.DependencyInjection;
using ResourceModelResource = CloudShell.ResourceModel.Resource;
using ResourceManagerClass = CloudShell.Abstractions.ResourceManager.ResourceClass;
using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;

namespace CloudShell.ControlPlane.Providers;

public sealed class ResourceModelGraphDnsZoneNameMappingReconciler(
    IEnumerable<INamePublishingProvider> namePublishingProviders,
    IEnumerable<IResourceModelResourceManagerEndpointProjectionProvider> endpointProjectionProviders,
    ResourceGraphModel graphModel,
    IServiceScopeFactory scopeFactory) :
    IDnsZoneNameMappingReconciler
{
    private readonly IReadOnlyList<INamePublishingProvider> _namePublishingProviders =
        namePublishingProviders.ToArray();
    private readonly IReadOnlyList<IResourceModelResourceManagerEndpointProjectionProvider> _endpointProjectionProviders =
        endpointProjectionProviders.ToArray();

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileNameMappingsAsync(
        ResourceModelResource resource,
        ResourceProjectionExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            var publishingResources = await ResolvePublishingResourcesAsync(
                context,
                cancellationToken);
            var publishingContext = CreatePublishingContext(
                resource,
                publishingResources);
            if (publishingContext.Definition.DnsNameMappings.Count == 0)
            {
                return
                [
                    new ResourceDefinitionDiagnostic(
                        ResourceDefinitionDiagnosticSeverity.Information,
                        "dns.zone.graphNameMappingsEmpty",
                        "No graph DNS name mappings to reconcile.",
                        resource.EffectiveResourceId)
                ];
            }

            var provider = GetNamePublishingProvider(publishingContext);
            if (provider is null)
            {
                return
                [
                    ResourceDefinitionDiagnostic.Error(
                        "dns.zone.graphNameMappingProviderMissing",
                        FormatMissingPublishingProviderMessage(
                            resource,
                            publishingContext.Definition.Provider),
                        resource.EffectiveResourceId)
                ];
            }

            var result = await provider.ReconcileAsync(publishingContext, cancellationToken);
            return
            [
                new ResourceDefinitionDiagnostic(
                    ResourceDefinitionDiagnosticSeverity.Information,
                    "dns.zone.graphNameMappingsReconciled",
                    result.Message,
                    resource.EffectiveResourceId)
            ];
        }
        catch (InvalidOperationException exception)
        {
            return
            [
                ResourceDefinitionDiagnostic.Error(
                    "dns.zone.graphNameMappingReconcileFailed",
                    exception.Message,
                    resource.EffectiveResourceId)
            ];
        }
    }

    private async ValueTask<IReadOnlyList<ResourceModelResource>> ResolvePublishingResourcesAsync(
        ResourceProjectionExecutionContext context,
        CancellationToken cancellationToken)
    {
        var snapshot = await graphModel.GetSnapshotAsync(cancellationToken);
        if (snapshot.Resources.Count == 0)
        {
            return context.Resources;
        }

        using var scope = scopeFactory.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<ResourceResolver>();
        return snapshot.Resources
            .Select(state => resolver.Resolve(state))
            .ToArray();
    }

    private DnsNamePublishingContext CreatePublishingContext(
        ResourceModelResource resource,
        IReadOnlyList<ResourceModelResource> resources)
    {
        var resourceManagerResources = resources
            .Select(ToResourceManagerResource)
            .ToArray();
        var resourceManager = new GraphDnsResourceManagerStore(resourceManagerResources);
        var zoneResource = resourceManager.GetResource(resource.EffectiveResourceId)
            ?? throw new InvalidOperationException(
                $"DNS zone resource '{resource.EffectiveResourceId}' could not be projected for graph name-mapping reconciliation.");
        var zoneDefinition = CreateZoneDefinition(resource, resources);
        var publisherResources = ResolveNamePublisherResources(resourceManager, zoneDefinition);
        var mappings = zoneDefinition.DnsNameMappings
            .Select(mapping => ResolveNameMapping(resourceManager, zoneDefinition, mapping, publisherResources))
            .ToArray();

        return new DnsNamePublishingContext(
            zoneResource,
            zoneDefinition,
            mappings,
            publisherResources,
            resourceManager);
    }

    private DnsZoneResourceDefinition CreateZoneDefinition(
        ResourceModelResource resource,
        IReadOnlyList<ResourceModelResource> resources) =>
        new(
            resource.EffectiveResourceId,
            resource.Name,
            resource.Attributes.GetString(DnsZoneResourceTypeProvider.Attributes.ZoneName) ?? resource.Name,
            resource.Attributes.GetString(DnsZoneResourceTypeProvider.Attributes.Provider),
            resources
                .Where(candidate =>
                    candidate.Type.TypeId == NameMappingResourceTypeProvider.ResourceTypeId &&
                    BelongsTo(candidate, resource.EffectiveResourceId))
                .Select(CreateNameMappingDefinition)
                .ToArray());

    private static DnsNameMappingDefinition CreateNameMappingDefinition(
        ResourceModelResource resource)
    {
        var targetResourceId = resource.State.ResourceDependencies
            .Select(reference => TryGetTargetReference(reference, out var resourceId, out var typeId)
                ? (ResourceId: resourceId, TypeId: typeId)
                : default)
            .FirstOrDefault(reference =>
                !string.IsNullOrWhiteSpace(reference.ResourceId) &&
                reference.TypeId != DnsZoneResourceTypeProvider.ResourceTypeId);
        if (string.IsNullOrWhiteSpace(targetResourceId.ResourceId))
        {
            throw new InvalidOperationException(
                $"Name-mapping resource '{resource.EffectiveResourceId}' does not declare a target graph resource dependency.");
        }

        return new(
            resource.EffectiveResourceId,
            resource.Name,
            resource.Attributes.GetString(NameMappingResourceTypeProvider.Attributes.HostName) ??
                resource.Name,
            targetResourceId.ResourceId,
            resource.Attributes.GetString(NameMappingResourceTypeProvider.Attributes.TargetEndpointName),
            ParseExposure(resource.Attributes.GetString(NameMappingResourceTypeProvider.Attributes.Exposure)));
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

    private INamePublishingProvider? GetNamePublishingProvider(
        DnsNamePublishingContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.Definition.Provider) &&
            !string.Equals(context.Definition.Provider, "logical", StringComparison.OrdinalIgnoreCase))
        {
            return _namePublishingProviders.FirstOrDefault(candidate =>
                string.Equals(candidate.ProviderName, context.Definition.Provider, StringComparison.OrdinalIgnoreCase) &&
                candidate.CanPublish(context));
        }

        return _namePublishingProviders.FirstOrDefault(candidate =>
            candidate.CanPublish(context));
    }

    private string FormatMissingPublishingProviderMessage(
        ResourceModelResource resource,
        string? requestedProvider)
    {
        var message =
            $"No activated DNS publishing provider can reconcile name mappings for DNS zone resource '{resource.EffectiveResourceId}'.";

        if (!string.IsNullOrWhiteSpace(requestedProvider) &&
            !string.Equals(requestedProvider, "logical", StringComparison.OrdinalIgnoreCase))
        {
            message += $" Requested provider: '{requestedProvider}'.";
        }

        if (_namePublishingProviders.Count == 0)
        {
            return message + " No DNS publishing providers are registered.";
        }

        var providerNames = string.Join(
            ", ",
            _namePublishingProviders
                .Select(provider => $"'{provider.ProviderName}'")
                .Order(StringComparer.OrdinalIgnoreCase));

        return message + $" Registered providers: {providerNames}.";
    }

    private static IReadOnlyList<ResourceManagerResource> ResolveNamePublisherResources(
        IResourceManagerStore resourceManager,
        DnsZoneResourceDefinition definition)
    {
        var publisherIds = definition.DnsNameMappings
            .Select(mapping => mapping.ProviderResourceId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var publisherResources = new List<ResourceManagerResource>();
        foreach (var publisherId in publisherIds)
        {
            var resource = resourceManager.GetResource(publisherId)
                ?? throw new InvalidOperationException(
                    $"DNS zone resource '{definition.Id}' references name publishing provider resource '{publisherId}', but the resource could not be found.");
            if (!resource.HasCapability(ResourceCapabilityIds.NetworkingNamePublisher))
            {
                throw new InvalidOperationException(
                    $"DNS zone resource '{definition.Id}' references provider resource '{publisherId}', but it does not advertise capability '{ResourceCapabilityIds.NetworkingNamePublisher}'.");
            }

            publisherResources.Add(resource);
        }

        return publisherResources;
    }

    private static DnsNameMappingResolution ResolveNameMapping(
        IResourceManagerStore resourceManager,
        DnsZoneResourceDefinition definition,
        DnsNameMappingDefinition mapping,
        IReadOnlyList<ResourceManagerResource> publisherResources)
    {
        var target = resourceManager.GetResource(mapping.TargetResourceId)
            ?? throw new InvalidOperationException(
                $"DNS zone resource '{definition.Id}' name mapping '{mapping.Id}' target resource '{mapping.TargetResourceId}' could not be found.");
        var targetEndpoint = ResolveNameMappingTargetEndpoint(definition, mapping, target);
        var publisherResource = string.IsNullOrWhiteSpace(mapping.ProviderResourceId)
            ? null
            : publisherResources.First(resource =>
                string.Equals(resource.Id, mapping.ProviderResourceId, StringComparison.OrdinalIgnoreCase));

        return new DnsNameMappingResolution(
            mapping,
            target,
            targetEndpoint,
            targetEndpoint is null
                ? null
                : target.GetEndpointNetworkMapping(targetEndpoint.Name),
            publisherResource);
    }

    private static ResourceEndpoint? ResolveNameMappingTargetEndpoint(
        DnsZoneResourceDefinition definition,
        DnsNameMappingDefinition mapping,
        ResourceManagerResource target)
    {
        if (string.IsNullOrWhiteSpace(mapping.TargetEndpointName))
        {
            return null;
        }

        return target.Endpoints.FirstOrDefault(endpoint =>
                string.Equals(endpoint.Name, mapping.TargetEndpointName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"DNS zone resource '{definition.Id}' name mapping '{mapping.Id}' target endpoint '{mapping.TargetEndpointName}' could not be found on resource '{mapping.TargetResourceId}'. {FormatAvailableEndpointNames(target)}");
    }

    private static string FormatAvailableEndpointNames(
        ResourceManagerResource resource)
    {
        if (resource.Endpoints.Count == 0)
        {
            return "The target resource has no projected endpoints.";
        }

        var endpointNames = string.Join(
            ", ",
            resource.Endpoints
                .Select(endpoint => $"'{endpoint.Name}'")
                .Order(StringComparer.OrdinalIgnoreCase));

        return $"Available endpoints: {endpointNames}.";
    }

    private static bool BelongsTo(
        ResourceModelResource resource,
        string dependencyResourceId) =>
        resource.State.ResourceDependencies.Any(reference =>
            reference.Relationship == ResourceReferenceRelationships.BelongsTo &&
            reference.TryGetResourceId(out var resourceId) &&
            string.Equals(resourceId, dependencyResourceId, StringComparison.OrdinalIgnoreCase));

    private static bool TryGetTargetReference(
        ResourceReference reference,
        out string resourceId,
        out ResourceTypeId? typeId)
    {
        resourceId = string.Empty;
        typeId = null;

        if (reference.Relationship == ResourceReferenceRelationships.Reference &&
            reference.TryGetResourceId(out resourceId))
        {
            typeId = reference.TypeId;
            return true;
        }

        return false;
    }

    private static ResourceExposureScope ParseExposure(string? value) =>
        Enum.TryParse<ResourceExposureScope>(
            value,
            ignoreCase: true,
            out var exposure)
            ? exposure
            : ResourceExposureScope.Public;

    private sealed class GraphDnsResourceManagerStore(
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
            GetResource(id: resourceId) is not null;
    }
}
