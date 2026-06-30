using System.Globalization;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Components;

namespace CloudShell.Hosting.ResourceManager;

public static class ResourceDependencyGraphProjection
{
    public static ResourceDependencyGraphModel Create(
        IReadOnlyList<Resource> resources,
        ResourceDependencyGraphProjectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(resources);

        var projectionOptions = options ?? new ResourceDependencyGraphProjectionOptions();
        var nodes = new Dictionary<string, ResourceDependencyGraphNode>(StringComparer.OrdinalIgnoreCase);
        var links = new Dictionary<string, ResourceDependencyGraphLink>(StringComparer.OrdinalIgnoreCase);
        var visibleResourceIds = resources
            .Select(resource => resource.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var relationshipResources = projectionOptions.RelationshipResources ?? resources;
        var relationshipResourcesById = relationshipResources
            .ToDictionary(resource => resource.Id, StringComparer.OrdinalIgnoreCase);
        var internetReachability = ResourceInternetReachabilityProjection.CreateMap(resources);

        foreach (var resource in resources)
        {
            nodes[resource.Id] = new ResourceDependencyGraphNode(
                resource.Id,
                projectionOptions.GetResourceLabel(resource),
                projectionOptions.GetResourceName(resource),
                resource.EffectiveTypeId,
                resource.ResourceClass.ToString(),
                ResourceDependencyGraphNodeKinds.Resource,
                GetEndpointText(resource),
                resource.State?.ToString() ?? "No lifecycle status",
                projectionOptions.GetStateClass(resource.State),
                projectionOptions.CreateResourceDetailUrl(resource),
                resource.Id);
        }

        foreach (var resource in resources)
        {
            foreach (var dependencyId in resource.DependsOn
                .Where(visibleResourceIds.Contains)
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var label = TryGetDatabaseServerResourceId(resource, out var serverResourceId) &&
                    string.Equals(dependencyId, serverResourceId, StringComparison.OrdinalIgnoreCase)
                        ? "hosted by"
                        : "depends on";
                AddLink(
                    links,
                    resource.Id,
                    dependencyId,
                    label,
                    ResourceDependencyGraphLinkKinds.Dependency,
                    resource.Id);
            }
        }

        AddDerivedResourceRelationshipLinks(resources, relationshipResourcesById, visibleResourceIds, links);

        if (projectionOptions.IncludeNetworkTopologyOverlay)
        {
            AddNetworkTopologyOverlay(resources, visibleResourceIds, internetReachability, nodes, links);
        }

        return new ResourceDependencyGraphModel(
            nodes.Values
                .OrderBy(node => GetNodeSortOrder(node.NodeKind))
                .ThenBy(node => node.Label, StringComparer.CurrentCultureIgnoreCase)
                .ToArray(),
            links.Values
                .OrderBy(link => GetLinkSortOrder(link.Kind))
                .ThenBy(link => link.Label, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(link => link.Source, StringComparer.OrdinalIgnoreCase)
                .ThenBy(link => link.Target, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            resources.Count,
            links.Values.Count(link => string.Equals(
                link.Kind,
                ResourceDependencyGraphLinkKinds.Dependency,
                StringComparison.OrdinalIgnoreCase)),
            links.Values.Count(link => string.Equals(
                link.Kind,
                ResourceDependencyGraphLinkKinds.Topology,
                StringComparison.OrdinalIgnoreCase)));
    }

    private static void AddNetworkTopologyOverlay(
        IReadOnlyList<Resource> resources,
        ISet<string> visibleResourceIds,
        IReadOnlyDictionary<string, string> internetReachability,
        IDictionary<string, ResourceDependencyGraphNode> nodes,
        IDictionary<string, ResourceDependencyGraphLink> links)
    {
        foreach (var network in resources)
        {
            foreach (var mapping in network.ResourceEndpointMappings)
            {
                if (visibleResourceIds.Contains(mapping.Source.ResourceId) &&
                    visibleResourceIds.Contains(mapping.Target.ResourceId))
                {
                    AddLink(
                        links,
                        mapping.Source.ResourceId,
                        mapping.Target.ResourceId,
                        "maps to",
                        ResourceDependencyGraphLinkKinds.Topology,
                        network.Id);
                }

                if (!string.IsNullOrWhiteSpace(mapping.ProviderResourceId) &&
                    visibleResourceIds.Contains(mapping.ProviderResourceId) &&
                    visibleResourceIds.Contains(mapping.Target.ResourceId))
                {
                    AddLink(
                        links,
                        mapping.ProviderResourceId,
                        mapping.Target.ResourceId,
                        "materializes",
                        ResourceDependencyGraphLinkKinds.Topology,
                        mapping.ProviderResourceId);
                }
            }
        }

        foreach (var loadBalancer in resources)
        {
            foreach (var route in loadBalancer.ResourceLoadBalancerRoutes)
            {
                if (visibleResourceIds.Contains(route.Target.ResourceId))
                {
                    AddLink(
                        links,
                        loadBalancer.Id,
                        route.Target.ResourceId,
                        "routes to",
                        ResourceDependencyGraphLinkKinds.Topology,
                        loadBalancer.Id);
                }
            }
        }

        foreach (var nameMapping in resources.Where(ResourceNameMappingDisplay.IsNameMappingResource))
        {
            if (!string.IsNullOrWhiteSpace(nameMapping.ParentResourceId) &&
                visibleResourceIds.Contains(nameMapping.ParentResourceId))
            {
                AddLink(
                    links,
                    nameMapping.ParentResourceId!,
                    nameMapping.Id,
                    "contains",
                    ResourceDependencyGraphLinkKinds.Topology,
                    nameMapping.ParentResourceId);
            }

            var targetResourceId = ResourceNameMappingDisplay.GetTargetResourceId(nameMapping);
            if (visibleResourceIds.Contains(targetResourceId))
            {
                AddLink(
                    links,
                    nameMapping.Id,
                    targetResourceId,
                    "names",
                    ResourceDependencyGraphLinkKinds.Topology,
                    nameMapping.Id);
            }

            var providerResourceId = ResourceNameMappingDisplay.GetProviderResourceId(nameMapping);
            if (visibleResourceIds.Contains(providerResourceId))
            {
                AddLink(
                    links,
                    providerResourceId,
                    nameMapping.Id,
                    "publishes",
                    ResourceDependencyGraphLinkKinds.Topology,
                    providerResourceId);
            }
        }

        foreach (var resource in resources.Where(resource => visibleResourceIds.Contains(resource.Id)))
        {
            if (internetReachability.TryGetValue(resource.Id, out var reachability) &&
                nodes.TryGetValue(resource.Id, out var node))
            {
                nodes[resource.Id] = node with
                {
                    InternetReachability = reachability
                };
            }
        }
    }

    private static void AddDerivedResourceRelationshipLinks(
        IReadOnlyList<Resource> resources,
        IReadOnlyDictionary<string, Resource> resourcesById,
        ISet<string> visibleResourceIds,
        IDictionary<string, ResourceDependencyGraphLink> links)
    {
        foreach (var resource in resources)
        {
            foreach (var dependencyId in resource.DependsOn.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!resourcesById.TryGetValue(dependencyId, out var dependency) ||
                    !TryGetDatabaseServerResourceId(dependency, out var serverResourceId) ||
                    visibleResourceIds.Contains(dependency.Id) ||
                    !visibleResourceIds.Contains(serverResourceId) ||
                    resource.DependsOn.Contains(serverResourceId, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                AddLink(
                    links,
                    resource.Id,
                    serverResourceId,
                    "uses database on",
                    ResourceDependencyGraphLinkKinds.Dependency,
                    resource.Id);
            }
        }
    }

    private static void AddLink(
        IDictionary<string, ResourceDependencyGraphLink> links,
        string source,
        string target,
        string label,
        string kind,
        string? resourceId)
    {
        if (string.IsNullOrWhiteSpace(source) ||
            string.IsNullOrWhiteSpace(target) ||
            string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var id = string.Join('\u001f', source, label, target);
        links[id] = new ResourceDependencyGraphLink(source, target, label, kind, resourceId);
    }

    private static string GetEndpointText(Resource resource)
    {
        var address = resource.ResourceEndpointNetworkMappings.FirstOrDefault()?.Address;
        if (!string.IsNullOrWhiteSpace(address))
        {
            return TryFormatEndpointHost(address) ?? address;
        }

        return resource.Endpoints.Count switch
        {
            0 => "No endpoints",
            1 => "1 endpoint",
            _ => string.Create(CultureInfo.InvariantCulture, $"{resource.Endpoints.Count} endpoints")
        };
    }

    private static bool TryGetDatabaseServerResourceId(Resource resource, out string serverResourceId)
    {
        if (resource.ResourceAttributes.TryGetValue(ResourceAttributeNames.DatabaseServerResourceId, out var projectedServerResourceId) &&
            !string.IsNullOrWhiteSpace(projectedServerResourceId))
        {
            serverResourceId = projectedServerResourceId.Trim();
            return true;
        }

        if (resource.ResourceAttributes.TryGetValue("database.server", out var graphServerResourceId) &&
            !string.IsNullOrWhiteSpace(graphServerResourceId))
        {
            serverResourceId = graphServerResourceId.Trim();
            return true;
        }

        if (IsSqlDatabaseResource(resource))
        {
            serverResourceId = resource.DependsOn.FirstOrDefault(dependencyId => !string.IsNullOrWhiteSpace(dependencyId)) ??
                string.Empty;
            return !string.IsNullOrWhiteSpace(serverResourceId);
        }

        serverResourceId = string.Empty;
        return false;
    }

    private static bool IsSqlDatabaseResource(Resource resource) =>
        string.Equals(resource.EffectiveTypeId, "application.sql-database", StringComparison.OrdinalIgnoreCase) ||
        resource.EffectiveTypeId.Contains("sql-database", StringComparison.OrdinalIgnoreCase);

    private static string? TryFormatEndpointHost(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri))
        {
            return null;
        }

        return uri.IsDefaultPort
            ? uri.Host
            : string.Create(CultureInfo.InvariantCulture, $"{uri.Host}:{uri.Port}");
    }

    private static int GetNodeSortOrder(string nodeKind) =>
        string.Equals(nodeKind, ResourceDependencyGraphNodeKinds.Resource, StringComparison.OrdinalIgnoreCase)
            ? 0
            : 1;

    private static int GetLinkSortOrder(string kind) =>
        string.Equals(kind, ResourceDependencyGraphLinkKinds.Dependency, StringComparison.OrdinalIgnoreCase)
            ? 0
            : 1;

}

public sealed record ResourceDependencyGraphModel(
    IReadOnlyList<ResourceDependencyGraphNode> Nodes,
    IReadOnlyList<ResourceDependencyGraphLink> Links,
    int ResourceCount,
    int DependencyCount,
    int TopologyCount)
{
    public static ResourceDependencyGraphModel Empty { get; } = new([], [], 0, 0, 0);
}

public sealed record ResourceDependencyGraphNode(
    string Id,
    string Label,
    string Name,
    string Type,
    string ResourceClass,
    string NodeKind,
    string EndpointText,
    string StateLabel,
    string StateClass,
    string? DetailUrl,
    string? ResourceId,
    string? InternetReachability = null);

public sealed record ResourceDependencyGraphLink(
    string Source,
    string Target,
    string Label,
    string Kind,
    string? ResourceId);

public sealed record ResourceDependencyGraphProjectionOptions
{
    public bool IncludeNetworkTopologyOverlay { get; init; }

    public IReadOnlyList<Resource>? RelationshipResources { get; init; }

    public Func<Resource, string> GetResourceLabel { get; init; } = static resource => resource.EffectiveDisplayName;

    public Func<Resource, string> GetResourceName { get; init; } = static resource => resource.Name;

    public Func<Resource, string?> CreateResourceDetailUrl { get; init; } = static _ => null;

    public Func<ResourceState?, string> GetStateClass { get; init; } = static _ => "state-unknown";
}

public static class ResourceDependencyGraphNodeKinds
{
    public const string Resource = "resource";
    public const string Topology = "topology";
}

public static class ResourceDependencyGraphLinkKinds
{
    public const string Dependency = "dependency";
    public const string Topology = "topology";
}

public static class ResourceDependencyGraphInternetReachability
{
    public const string Reachable = "reachable";
    public const string Inferred = "inferred";
}
