using System.Globalization;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Providers.Traefik;
using CloudShell.ResourceModel;
using CloudShell.ControlPlane.Providers;
using ResourceModelResource = CloudShell.ResourceModel.Resource;
using ResourceManagerClass = CloudShell.Abstractions.ResourceManager.ResourceClass;
using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;

namespace CloudShell.Providers.Traefik;

public sealed class ResourceModelGraphTraefikLoadBalancerConfigurationApplier(
    TraefikLoadBalancerProvider provider,
    IEnumerable<ICertificateReferenceResolver>? certificateResolvers = null) : ILoadBalancerConfigurationApplier
{
    private readonly IReadOnlyList<ICertificateReferenceResolver> _certificateResolvers =
        certificateResolvers?.ToArray() ?? [];

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyConfigurationAsync(
        ResourceModelResource resource,
        ResourceProjectionExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        LoadBalancerProviderContext providerContext;
        try
        {
            providerContext = await CreateProviderContextAsync(
                resource,
                context,
                cancellationToken);
        }
        catch (ResourceSettingResolutionException exception)
        {
            return
            [
                ResourceDefinitionDiagnostic.Error(
                    "network.loadBalancer.certificateResolutionFailed",
                    exception.Message,
                    resource.EffectiveResourceId)
            ];
        }

        ResourceProcedureResult result;
        try
        {
            result = await provider.ApplyAsync(providerContext, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return
            [
                ResourceDefinitionDiagnostic.Error(
                    "network.loadBalancer.traefikConfigurationFailed",
                    exception.Message,
                    resource.EffectiveResourceId)
            ];
        }

        return
        [
            new ResourceDefinitionDiagnostic(
                ResourceDefinitionDiagnosticSeverity.Information,
                "network.loadBalancer.traefikConfigurationApplied",
                result.Message,
                resource.EffectiveResourceId)
        ];
    }

    private async ValueTask<LoadBalancerProviderContext> CreateProviderContextAsync(
        ResourceModelResource resource,
        ResourceProjectionExecutionContext context,
        CancellationToken cancellationToken)
    {
        var definition = CreateDefinition(resource);
        var hostResource = ResolveHostResource(resource, context);
        var routes = definition.LoadBalancerRoutes
            .Select(route => ResolveRoute(resource, context, route))
            .ToArray();
        var certificates = await ResolveCertificatesAsync(
            definition,
            cancellationToken);

        return new(
            ToResourceManagerResource(resource),
            definition,
            hostResource,
            routes,
            new GraphLoadBalancerResourceManagerStore(context.Resources.Select(ToResourceManagerResource).ToArray()),
            certificates);
    }

    private async ValueTask<IReadOnlyList<LoadBalancerResolvedCertificate>> ResolveCertificatesAsync(
        LoadBalancerResourceDefinition definition,
        CancellationToken cancellationToken)
    {
        var certificates = new List<LoadBalancerResolvedCertificate>();
        foreach (var entrypoint in definition.LoadBalancerEntrypoints)
        {
            if (entrypoint.Certificate is null)
            {
                continue;
            }

            var certificate = await ResolveCertificateAsync(
                entrypoint.Name,
                entrypoint.Certificate,
                definition.Id,
                cancellationToken);
            certificates.Add(certificate);
        }

        return certificates;
    }

    private async ValueTask<LoadBalancerResolvedCertificate> ResolveCertificateAsync(
        string entrypointName,
        CertificateReference reference,
        string loadBalancerResourceId,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var context = new ResourceSettingResolutionContext(
            loadBalancerResourceId,
            Operation: LoadBalancerResourceTypeProvider.Operations.ApplyConfiguration.ToString());
        foreach (var resolver in _certificateResolvers)
        {
            var result = await resolver.ResolveCertificateAsync(
                reference,
                context,
                cancellationToken);
            if (result.IsResolved)
            {
                return new LoadBalancerResolvedCertificate(
                    entrypointName,
                    reference,
                    result.Value ?? string.Empty,
                    result.ContentType,
                    result.Thumbprint,
                    result.Subject,
                    result.NotBefore,
                    result.Expires);
            }

            if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                errors.Add(result.ErrorMessage);
            }
        }

        var message = errors.Count == 0
            ? $"No vault provider can resolve certificate '{reference.CertificateName}' from '{reference.VaultResourceId}'."
            : string.Join(" ", errors);
        throw new ResourceSettingResolutionException(entrypointName, "certificate", message);
    }

    private static LoadBalancerResourceDefinition CreateDefinition(ResourceModelResource resource)
    {
        var entrypoints = resource.Attributes
            .GetObject<IReadOnlyList<LoadBalancerEntrypointValue>>(
                LoadBalancerResourceTypeProvider.Attributes.Entrypoints) ?? [];
        var routes = resource.Attributes
            .GetObject<IReadOnlyList<LoadBalancerRouteValue>>(
                LoadBalancerResourceTypeProvider.Attributes.Routes) ?? [];

        return new(
            resource.EffectiveResourceId,
            resource.Name,
            resource.Attributes.GetString(LoadBalancerResourceTypeProvider.Attributes.Provider) ?? "traefik",
            resource.Attributes.GetString(LoadBalancerResourceTypeProvider.Attributes.HostResourceId),
            entrypoints.Select(ToResourceManagerEntrypoint).ToArray(),
            routes.Select(ToResourceManagerRoute).ToArray());
    }

    private static LoadBalancerEntrypoint ToResourceManagerEntrypoint(
        LoadBalancerEntrypointValue value) =>
        new(
            value.Name,
            ParseEnum(value.Protocol, ResourceEndpointProtocol.Http),
            value.Port,
            ParseEnum(value.Exposure, ResourceExposureScope.Public),
            ToCertificateReference(value.CertificateRef));

    private static CertificateReference? ToCertificateReference(
        LoadBalancerCertificateReferenceValue? value) =>
        value is null
            ? null
            : new CertificateReference(
                value.VaultResourceId,
                value.Name,
                string.IsNullOrWhiteSpace(value.Version) ? null : value.Version);

    private static LoadBalancerRoute ToResourceManagerRoute(
        LoadBalancerRouteValue value)
    {
        if (!value.Target.Resource.TryGetResourceId(out var targetResourceId))
        {
            throw new InvalidOperationException(
                $"Load balancer route '{value.Id}' target does not use resource-id addressing.");
        }

        return new(
            value.Id,
            value.Name,
            ParseEnum(value.Kind, LoadBalancerRouteKind.Http),
            value.EntrypointName,
            new LoadBalancerRouteMatch(
                value.Match.Host,
                value.Match.PathPrefix,
                value.Match.Port),
            new LoadBalancerRouteTarget(
                targetResourceId,
                value.Target.EndpointName,
                value.Target.Port));
    }

    private static ResourceManagerResource? ResolveHostResource(
        ResourceModelResource resource,
        ResourceProjectionExecutionContext context)
    {
        var hostResourceId = resource.Attributes.GetString(
            LoadBalancerResourceTypeProvider.Attributes.HostResourceId);
        return string.IsNullOrWhiteSpace(hostResourceId)
            ? null
            : context.FindResource(hostResourceId) is { } host
                ? ToResourceManagerResource(host)
                : null;
    }

    private static LoadBalancerRouteResolution ResolveRoute(
        ResourceModelResource loadBalancer,
        ResourceProjectionExecutionContext context,
        LoadBalancerRoute route)
    {
        var targetResource = context.FindResource(route.Target.ResourceId)
            ?? throw new InvalidOperationException(
                $"Load balancer resource '{loadBalancer.EffectiveResourceId}' route '{route.Id}' target resource '{route.Target.ResourceId}' could not be found in the graph projection.");
        var target = ToResourceManagerResource(targetResource);

        return new(
            route,
            target,
            null,
            ResolveContainerBackends(route, targetResource),
            null);
    }

    private static IReadOnlyList<LoadBalancerBackendTarget> ResolveContainerBackends(
        LoadBalancerRoute route,
        ResourceModelResource targetResource)
    {
        if (route.Target.Port is not { } port ||
            !string.Equals(
                targetResource.Type.TypeId.ToString(),
                ContainerApplicationResourceTypeProvider.ResourceTypeId.ToString(),
                StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var replicas = ResolveContainerReplicaCount(targetResource);
        var protocol = route.Kind == LoadBalancerRouteKind.Http ? "http" : "tcp";
        var serviceName = ResourceOrchestratorReplicaGroups.CreateDefaultServiceName(
            targetResource.EffectiveResourceId);
        return Enumerable
            .Range(1, replicas)
            .Select(replica => new LoadBalancerBackendTarget(
                ResourceOrchestratorReplicaGroups.CreateDefaultInstanceName(
                    serviceName,
                    replica,
                    replicas),
                port,
                protocol))
            .ToArray();
    }

    private static int ResolveContainerReplicaCount(ResourceModelResource resource)
    {
        var value = resource.Attributes.GetString(
            ContainerApplicationResourceTypeProvider.Attributes.ContainerReplicas);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var replicas)
            ? Math.Max(1, replicas)
            : 1;
    }

    private static ResourceManagerResource ToResourceManagerResource(
        ResourceModelResource resource) =>
        new(
            Id: resource.EffectiveResourceId,
            Name: resource.Name,
            Kind: resource.Type.TypeId.ToString(),
            Provider: resource.State.ProviderId ?? resource.Type.Definition.DefaultProviderId ?? "resource-model",
            Region: "local",
            State: null,
            Endpoints: [],
            Version: resource.Version ?? resource.Revision.ToString(),
            LastUpdated: resource.LastModifiedAt ?? resource.CreatedAt ?? DateTimeOffset.UnixEpoch,
            DependsOn: resource.State.StartupDependencyIds,
            Attributes: resource.Attributes
                .Where(attribute => attribute.Value is not null)
                .ToDictionary(
                    attribute => attribute.Name.ToString(),
                    attribute => attribute.Value!,
                    StringComparer.OrdinalIgnoreCase),
            TypeId: resource.Type.TypeId.ToString(),
            ResourceClass: ParseResourceClass(resource.Class.ClassId.ToString()),
            DisplayName: resource.State.DisplayName);

    private static ResourceManagerClass ParseResourceClass(string value) =>
        Enum.TryParse<ResourceManagerClass>(value, ignoreCase: true, out var resourceClass)
            ? resourceClass
            : ResourceManagerClass.Generic;

    private static TValue ParseEnum<TValue>(
        string? value,
        TValue fallback)
        where TValue : struct =>
        !string.IsNullOrWhiteSpace(value) &&
        Enum.TryParse<TValue>(value, ignoreCase: true, out var parsed)
            ? parsed
            : fallback;

    private sealed class GraphLoadBalancerResourceManagerStore(
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

        public ResourceManagerResource? GetResource(string resourceId) =>
            resources.FirstOrDefault(resource =>
                string.Equals(resource.Id, resourceId, StringComparison.OrdinalIgnoreCase));

        public IReadOnlyList<ResourceManagerResource> GetChildren(string resourceId) => [];

        public ResourceGroup? GetGroupForResource(string resourceId) => null;

        public bool IsRegistered(string resourceId) =>
            GetResource(resourceId) is not null;
    }
}
