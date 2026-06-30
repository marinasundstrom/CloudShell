using System.Globalization;
using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Components;

namespace CloudShell.Hosting.ResourceManager;

public static class EnvironmentRuntimeMapProjection
{
    public static EnvironmentRuntimeMap Create(
        IReadOnlyList<Resource> resources,
        IReadOnlyList<ResourceDeploymentRecord> deployments,
        IReadOnlyList<EnvironmentRuntimeMapReplicaGroup> replicaGroups,
        EnvironmentRuntimeMapProjectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(deployments);
        ArgumentNullException.ThrowIfNull(replicaGroups);

        var context = new ProjectionContext(options ?? new EnvironmentRuntimeMapProjectionOptions());
        return context.Create(resources, deployments, replicaGroups);
    }

    private sealed class ProjectionContext(EnvironmentRuntimeMapProjectionOptions options)
    {
        public EnvironmentRuntimeMap Create(
            IReadOnlyList<Resource> resources,
            IReadOnlyList<ResourceDeploymentRecord> deployments,
            IReadOnlyList<EnvironmentRuntimeMapReplicaGroup> replicaGroups)
        {
            var nodes = new Dictionary<string, EnvironmentRuntimeMapNode>(StringComparer.OrdinalIgnoreCase);
            var links = new Dictionary<string, EnvironmentRuntimeMapLink>(StringComparer.OrdinalIgnoreCase);
            var groups = new Dictionary<string, EnvironmentRuntimeMapGroupBuilder>(StringComparer.OrdinalIgnoreCase);
            var resourceNodeIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var resourcesById = resources.ToDictionary(resource => resource.Id, StringComparer.OrdinalIgnoreCase);
            var serviceNodeIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var replicaGroupNodeIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var activeReplicaGroupRows = replicaGroups.ToDictionary(replicaGroup => replicaGroup.ReplicaGroupId, StringComparer.OrdinalIgnoreCase);
            var internetReachability = ResourceInternetReachabilityProjection.CreateMap(
                resources,
                includeImplicitHostNetwork: options.IncludeNetworkTopologyOverlay);
            var topologyResourceIds = options.IncludeNetworkTopologyOverlay
                ? CreateTopologyResourceIds(resources, internetReachability)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var routingNodeCount = 0;
            var networkTopologyNodeCount = 0;

            foreach (var resource in resources
                .Where(resource => ShouldShowEnvironmentMapResource(resource, options.IncludeNetworkTopologyOverlay, topologyResourceIds))
                .OrderBy(resource => resource.EffectiveDisplayName, StringComparer.CurrentCultureIgnoreCase))
            {
                var nodeId = CreateNodeId("resource", resource.Id);
                resourceNodeIds[resource.Id] = nodeId;
                nodes[nodeId] = new EnvironmentRuntimeMapNode(
                    nodeId,
                    resource.EffectiveDisplayName,
                    options.GetResourceTypeLabel(resource),
                    options.GetResourceIconName(resource),
                    resource.ResourceClass.ToString(),
                    IsContainerReplicaResource(resource) ? "replica" : "resource",
                    CreateResourceMapSummary(resource),
                    resource.State?.ToString() ?? Text("No lifecycle status"),
                    options.GetStateClass(resource.State),
                    options.CreateResourceDetailUrl(resource),
                    IsContainerReplicaResource(resource) ? EnvironmentRuntimeArtifactKinds.Replica : EnvironmentRuntimeArtifactKinds.Resource,
                    resource.Id,
                    GetAttribute(resource, ResourceAttributeNames.DeploymentServiceId),
                    GetAttribute(resource, ResourceAttributeNames.DeploymentReplicaGroupId),
                    GetAttribute(resource, ResourceAttributeNames.RuntimeRevision),
                    internetReachability.GetValueOrDefault(resource.Id),
                    options.IncludeNetworkTopologyOverlay
                        ? ResourceEndpointDisplay.GetPreferredEndpointText(resource, resources)
                        : null);
            }

            if (options.IncludeNetworkTopologyOverlay)
            {
                AddInferredHostNetworkNode(
                    resources,
                    nodes,
                    links,
                    resourceNodeIds,
                    ref networkTopologyNodeCount);
            }

            foreach (var resource in resources.Where(IsContainerApplicationResource))
            {
                var serviceId = GetContainerApplicationServiceId(resource);
                var serviceNode = EnsureServiceNode(
                    nodes,
                    serviceNodeIds,
                    serviceId,
                    resource.EffectiveDisplayName,
                    GetOptional(resource.ResourceAttributes.GetValueOrDefault(ResourceAttributeNames.ContainerImage)),
                    resource.State?.ToString() ?? Text("Container application"),
                    options.GetStateClass(resource.State),
                    options.CreateResourceDetailUrl(resource),
                    resource.Id,
                    GetAttribute(resource, ResourceAttributeNames.DeploymentRevision));

                var groupId = CreateGroupId("service", serviceId);
                var serviceGroup = EnsureGroup(groups, groupId, resource.EffectiveDisplayName, "service", null);
                serviceGroup.ArtifactKind = EnvironmentRuntimeArtifactKinds.OrchestrationService;
                serviceGroup.ResourceId = resource.Id;
                serviceGroup.ServiceId = serviceId;
                serviceGroup.RuntimeRevisionId = GetAttribute(resource, ResourceAttributeNames.DeploymentRevision);
                serviceGroup.SetBadgeLabel(Text("Container app"));
                serviceGroup.NodeIds.Add(serviceNode.Id);
                serviceGroup.DetailUrl = serviceNode.DetailUrl;
            }

            foreach (var deployment in deployments.Where(deployment => IsProjected(deployment.ServiceId)))
            {
                var label = resourcesById.TryGetValue(deployment.SourceResourceId, out var sourceResource)
                    ? sourceResource.EffectiveDisplayName
                    : deployment.ServiceId;
                var detailUrl = resourceNodeIds.TryGetValue(deployment.SourceResourceId, out var sourceNodeId)
                    ? nodes[sourceNodeId].DetailUrl
                    : resourcesById.TryGetValue(deployment.SourceResourceId, out sourceResource)
                        ? options.CreateResourceDetailUrl(sourceResource)
                        : null;
                var serviceNode = EnsureServiceNode(
                    nodes,
                    serviceNodeIds,
                    deployment.ServiceId,
                    label,
                    deployment.EnvironmentRevisionNumber is { } revisionNumber
                        ? Text("Revision {0}", revisionNumber)
                        : GetOptional(deployment.EnvironmentRevisionId),
                    deployment.Status.ToString(),
                    "state-running",
                    detailUrl,
                    deployment.SourceResourceId,
                    deployment.RuntimeRevisionId);

                var serviceGroup = EnsureGroup(
                    groups,
                    CreateGroupId("service", deployment.ServiceId),
                    label,
                    "service",
                    null);
                serviceGroup.ArtifactKind = EnvironmentRuntimeArtifactKinds.OrchestrationService;
                serviceGroup.ResourceId = deployment.SourceResourceId;
                serviceGroup.ServiceId = deployment.ServiceId;
                serviceGroup.RuntimeRevisionId = deployment.RuntimeRevisionId;
                serviceGroup.NodeIds.Add(serviceNode.Id);

                if (resourceNodeIds.TryGetValue(deployment.SourceResourceId, out var sourceResourceNodeId))
                {
                    AddLink(
                        links,
                        sourceResourceNodeId,
                        serviceNode.Id,
                        Text("creates service"),
                        "deployment",
                        EnvironmentRuntimeArtifactKinds.Deployment,
                        deployment.SourceResourceId,
                        deployment.ServiceId,
                        deployment.ReplicaGroup?.Id,
                        deployment.RuntimeRevisionId);
                }
            }

            foreach (var replicaGroup in replicaGroups)
            {
                var replicaNodeId = CreateNodeId("replica-group", replicaGroup.ReplicaGroupId);
                replicaGroupNodeIds[replicaGroup.ReplicaGroupId] = replicaNodeId;
                nodes[replicaNodeId] = new EnvironmentRuntimeMapNode(
                    replicaNodeId,
                    replicaGroup.ReplicaGroupId,
                    "Replica group",
                    "container",
                    ResourceClass.Container.ToString(),
                    "replica-group",
                    Text(
                        "{0} of {1} slot(s), {2} materialized",
                        replicaGroup.OccupiedSlots,
                        replicaGroup.RequestedSlots,
                        replicaGroup.MaterializedReplicas),
                    GetOptional(replicaGroup.RuntimeRevisionId),
                    replicaGroup.RepairFailedCount > 0
                        ? "state-degraded"
                        : replicaGroup.RepairingCount > 0
                            ? "state-starting"
                            : "state-running",
                    serviceNodeIds.TryGetValue(replicaGroup.ServiceId, out var serviceNodeId) &&
                        nodes.TryGetValue(serviceNodeId, out var serviceNode)
                            ? serviceNode.DetailUrl
                            : null,
                    EnvironmentRuntimeArtifactKinds.ReplicaGroup,
                    replicaGroup.SourceResourceId,
                    replicaGroup.ServiceId,
                    replicaGroup.ReplicaGroupId,
                    replicaGroup.RuntimeRevisionId);

                if (serviceNodeIds.TryGetValue(replicaGroup.ServiceId, out var owningServiceNodeId))
                {
                    AddLink(
                        links,
                        owningServiceNodeId,
                        replicaNodeId,
                        Text("owns"),
                        "orchestration",
                        EnvironmentRuntimeArtifactKinds.OrchestrationService,
                        replicaGroup.SourceResourceId,
                        replicaGroup.ServiceId,
                        replicaGroup.ReplicaGroupId,
                        replicaGroup.RuntimeRevisionId);
                }

                var serviceGroupId = CreateGroupId("service", replicaGroup.ServiceId);
                if (groups.TryGetValue(serviceGroupId, out var serviceGroup))
                {
                    serviceGroup.NodeIds.Add(replicaNodeId);
                }

                var replicaGroupBoundary = EnsureGroup(
                    groups,
                    CreateGroupId("replica-group", replicaGroup.ReplicaGroupId),
                    replicaGroup.ReplicaGroupId,
                    "replica-group",
                    serviceGroupId);
                replicaGroupBoundary.ArtifactKind = EnvironmentRuntimeArtifactKinds.ReplicaGroup;
                replicaGroupBoundary.ResourceId = replicaGroup.SourceResourceId;
                replicaGroupBoundary.ServiceId = replicaGroup.ServiceId;
                replicaGroupBoundary.ReplicaGroupId = replicaGroup.ReplicaGroupId;
                replicaGroupBoundary.RuntimeRevisionId = replicaGroup.RuntimeRevisionId;
                replicaGroupBoundary.NodeIds.Add(replicaNodeId);
            }

            foreach (var replica in resources.Where(IsContainerReplicaResource))
            {
                var owningResourceId = FirstNonEmpty(replica.OwnerResourceId, replica.ParentResourceId);
                var serviceId = GetReplicaServiceId(replica, owningResourceId, resourcesById);
                var replicaGroupId = GetReplicaGroupId(replica, owningResourceId, resourcesById, activeReplicaGroupRows, serviceId);
                var runtimeRevisionId = FirstNonEmpty(
                    GetAttribute(replica, ResourceAttributeNames.RuntimeRevision),
                    owningResourceId is not null && resourcesById.TryGetValue(owningResourceId, out var owner)
                        ? GetAttribute(owner, ResourceAttributeNames.DeploymentRevision)
                        : null);

                if (!serviceNodeIds.ContainsKey(serviceId) &&
                    owningResourceId is not null &&
                    resourcesById.TryGetValue(owningResourceId, out var ownerResource))
                {
                    EnsureServiceNode(
                        nodes,
                        serviceNodeIds,
                        serviceId,
                        ownerResource.EffectiveDisplayName,
                        GetOptional(ownerResource.ResourceAttributes.GetValueOrDefault(ResourceAttributeNames.ContainerImage)),
                        ownerResource.State?.ToString() ?? Text("Container application"),
                        options.GetStateClass(ownerResource.State),
                        options.CreateResourceDetailUrl(ownerResource),
                        ownerResource.Id,
                        GetAttribute(ownerResource, ResourceAttributeNames.DeploymentRevision));
                }

                if (!replicaGroupNodeIds.TryGetValue(replicaGroupId, out var replicaGroupNodeId))
                {
                    replicaGroupNodeId = CreateNodeId("replica-group", replicaGroupId);
                    replicaGroupNodeIds[replicaGroupId] = replicaGroupNodeId;
                    nodes[replicaGroupNodeId] = new EnvironmentRuntimeMapNode(
                        replicaGroupNodeId,
                        replicaGroupId,
                        "Replica group",
                        "container",
                        ResourceClass.Container.ToString(),
                        "replica-group",
                        Text("Materialized replica resources"),
                        GetOptional(runtimeRevisionId),
                        "state-running",
                        owningResourceId is not null &&
                            resourceNodeIds.TryGetValue(owningResourceId, out var ownerNodeId) &&
                            nodes.TryGetValue(ownerNodeId, out var ownerNode)
                                ? ownerNode.DetailUrl
                                : null,
                        EnvironmentRuntimeArtifactKinds.ReplicaGroup,
                        owningResourceId,
                        serviceId,
                        replicaGroupId,
                        runtimeRevisionId);
                }

                if (serviceNodeIds.TryGetValue(serviceId, out var serviceNodeId))
                {
                    AddLink(
                        links,
                        serviceNodeId,
                        replicaGroupNodeId,
                        Text("owns"),
                        "orchestration",
                        EnvironmentRuntimeArtifactKinds.OrchestrationService,
                        owningResourceId,
                        serviceId,
                        replicaGroupId,
                        runtimeRevisionId);
                }

                if (resourceNodeIds.TryGetValue(replica.Id, out var replicaNodeId))
                {
                    if (nodes.TryGetValue(replicaNodeId, out var replicaNode))
                    {
                        nodes[replicaNodeId] = replicaNode with
                        {
                            ServiceId = serviceId,
                            ReplicaGroupId = replicaGroupId,
                            RuntimeRevisionId = runtimeRevisionId
                        };
                    }

                    AddLink(
                        links,
                        replicaGroupNodeId,
                            replicaNodeId,
                            Text("contains"),
                            "orchestration",
                            EnvironmentRuntimeArtifactKinds.Replica,
                            replica.Id,
                            serviceId,
                            replicaGroupId,
                            runtimeRevisionId);

                    var serviceGroupId = CreateGroupId("service", serviceId);
                    EnsureGroup(groups, serviceGroupId, serviceId, "service", null).NodeIds.Add(replicaNodeId);
                    var replicaGroupBoundary = EnsureGroup(
                        groups,
                        CreateGroupId("replica-group", replicaGroupId),
                        replicaGroupId,
                        "replica-group",
                        serviceGroupId);
                    replicaGroupBoundary.ArtifactKind = EnvironmentRuntimeArtifactKinds.ReplicaGroup;
                    replicaGroupBoundary.ResourceId = owningResourceId;
                    replicaGroupBoundary.ServiceId = serviceId;
                    replicaGroupBoundary.ReplicaGroupId = replicaGroupId;
                    replicaGroupBoundary.RuntimeRevisionId = runtimeRevisionId;
                    replicaGroupBoundary.NodeIds.Add(replicaNodeId);
                }

                if (groups.TryGetValue(CreateGroupId("service", serviceId), out var serviceGroup))
                {
                    serviceGroup.NodeIds.Add(replicaGroupNodeId);
                    serviceGroup.ArtifactKind = EnvironmentRuntimeArtifactKinds.OrchestrationService;
                    serviceGroup.ResourceId ??= owningResourceId;
                    serviceGroup.ServiceId = serviceId;
                    serviceGroup.RuntimeRevisionId ??= runtimeRevisionId;
                    if (serviceNodeIds.TryGetValue(serviceId, out var owningServiceNodeId))
                    {
                        serviceGroup.NodeIds.Add(owningServiceNodeId);
                    }
                }

                var replicaBoundary = EnsureGroup(
                    groups,
                    CreateGroupId("replica-group", replicaGroupId),
                    replicaGroupId,
                    "replica-group",
                    CreateGroupId("service", serviceId));
                replicaBoundary.ArtifactKind = EnvironmentRuntimeArtifactKinds.ReplicaGroup;
                replicaBoundary.ResourceId ??= owningResourceId;
                replicaBoundary.ServiceId = serviceId;
                replicaBoundary.ReplicaGroupId = replicaGroupId;
                replicaBoundary.RuntimeRevisionId ??= runtimeRevisionId;
                replicaBoundary.NodeIds.Add(replicaGroupNodeId);
            }

            AddResourceRelationshipLinks(
                resources,
                links,
                resourceNodeIds,
                serviceNodeIds,
                options.IncludeDependencyRelationships);
            AddRoutingNodes(
                resources,
                deployments,
                internetReachability,
                nodes,
                links,
                groups,
                resourceNodeIds,
                serviceNodeIds,
                replicaGroupNodeIds,
                ref routingNodeCount,
                ref networkTopologyNodeCount);

            return new EnvironmentRuntimeMap(
                nodes.Values
                    .OrderBy(node => GetNodeSortOrder(node.NodeKind))
                    .ThenBy(node => node.Label, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray(),
                groups.Values
                    .Where(group => group.NodeIds.Count > 1 || !string.IsNullOrWhiteSpace(group.BadgeLabel))
                    .OrderBy(group => GetGroupSortOrder(group.Kind))
                    .ThenBy(group => group.Label, StringComparer.CurrentCultureIgnoreCase)
                    .Select(group => group.ToModel())
                    .ToArray(),
                links.Values
                    .OrderBy(link => link.Kind, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(link => link.Label, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(link => link.Source, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                resourceNodeIds.Count,
                serviceNodeIds.Count,
                routingNodeCount,
                networkTopologyNodeCount);
        }

        private EnvironmentRuntimeMapNode EnsureServiceNode(
            IDictionary<string, EnvironmentRuntimeMapNode> nodes,
            IDictionary<string, string> serviceNodeIds,
            string serviceId,
            string label,
            string summary,
            string stateLabel,
            string stateClass,
            string? detailUrl,
            string? sourceResourceId,
            string? runtimeRevisionId)
        {
            var serviceNodeId = CreateNodeId("service", serviceId);
            serviceNodeIds[serviceId] = serviceNodeId;
            if (nodes.TryGetValue(serviceNodeId, out var existing))
            {
                return existing;
            }

            var node = new EnvironmentRuntimeMapNode(
                serviceNodeId,
                label,
                "Orchestrator service",
                "service",
                ResourceClass.Service.ToString(),
                "service",
                summary,
                stateLabel,
                stateClass,
                detailUrl,
                EnvironmentRuntimeArtifactKinds.OrchestrationService,
                sourceResourceId,
                serviceId,
                null,
                runtimeRevisionId);
            nodes[serviceNodeId] = node;
            return node;
        }

        private static EnvironmentRuntimeMapGroupBuilder EnsureGroup(
            IDictionary<string, EnvironmentRuntimeMapGroupBuilder> groups,
            string id,
            string label,
            string kind,
            string? parentGroupId)
        {
            if (groups.TryGetValue(id, out var group))
            {
                return group;
            }

            group = new EnvironmentRuntimeMapGroupBuilder(id, label, kind, parentGroupId);
            groups[id] = group;
            return group;
        }

        private string CreateResourceMapSummary(Resource resource)
        {
            var parts = new List<string>();
            if (resource.DependsOn.Count > 0)
            {
                parts.Add(Text("{0} dependency/dependencies", resource.DependsOn.Count));
            }

            if (resource.Endpoints.Count > 0)
            {
                parts.Add(Text("{0} endpoint(s)", resource.Endpoints.Count));
            }

            if (resource.ResourceLoadBalancerRoutes.Count > 0)
            {
                parts.Add(Text("{0} route(s)", resource.ResourceLoadBalancerRoutes.Count));
            }

            if (ResourceNameMappingDisplay.IsNameMappingResource(resource))
            {
                parts.Add(ResourceNameMappingDisplay.GetHostName(resource));
            }

            return parts.Count == 0
                ? resource.PrimaryEndpoint
                : string.Join(" / ", parts);
        }

        private void AddRoutingNodes(
            IReadOnlyList<Resource> resources,
            IReadOnlyList<ResourceDeploymentRecord> deployments,
            IReadOnlyDictionary<string, string> internetReachability,
            IDictionary<string, EnvironmentRuntimeMapNode> nodes,
            IDictionary<string, EnvironmentRuntimeMapLink> links,
            IDictionary<string, EnvironmentRuntimeMapGroupBuilder> groups,
            IReadOnlyDictionary<string, string> resourceNodeIds,
            IReadOnlyDictionary<string, string> serviceNodeIds,
            IReadOnlyDictionary<string, string> replicaGroupNodeIds,
            ref int routingNodeCount,
            ref int networkTopologyNodeCount)
        {
            foreach (var deployment in deployments
                .OrderByDescending(deployment => deployment.CompletedAt ?? deployment.StartedAt))
            {
                foreach (var binding in deployment.Definition?.DeploymentServices
                    .SelectMany(service => service.RoutingBindingDefinitions) ?? [])
                {
                    var bindingNodeId = CreateNodeId("routing", binding.Name);
                    nodes[bindingNodeId] = new EnvironmentRuntimeMapNode(
                        bindingNodeId,
                        binding.Name,
                        "Routing binding",
                        "route",
                        ResourceClass.Network.ToString(),
                        "routing",
                        $"{binding.SourceEndpoint.ResourceId}/{binding.SourceEndpoint.EndpointName} -> {binding.ReplicaGroupName}",
                        Text("Binding"),
                        "state-running",
                        null,
                        EnvironmentRuntimeArtifactKinds.RoutingBinding,
                        binding.SourceEndpoint.ResourceId,
                        deployment.ServiceId,
                        binding.ReplicaGroupName,
                        deployment.RuntimeRevisionId);
                    routingNodeCount++;

                    var serviceId = FirstNonEmpty(binding.ServiceName, deployment.ServiceId);
                    if (serviceId is not null &&
                        serviceNodeIds.TryGetValue(serviceId, out var serviceNodeId))
                    {
                        AddLink(
                            links,
                            serviceNodeId,
                            bindingNodeId,
                            Text("routes"),
                            "orchestration",
                            EnvironmentRuntimeArtifactKinds.OrchestrationService,
                            binding.SourceEndpoint.ResourceId,
                            serviceId,
                            binding.ReplicaGroupName,
                            deployment.RuntimeRevisionId);

                        var serviceGroupId = CreateGroupId("service", serviceId);
                        if (groups.TryGetValue(serviceGroupId, out var serviceGroup))
                        {
                            serviceGroup.NodeIds.Add(bindingNodeId);
                        }
                    }

                    if (resourceNodeIds.TryGetValue(binding.SourceEndpoint.ResourceId, out var sourceNodeId))
                    {
                        AddLink(
                            links,
                            sourceNodeId,
                            bindingNodeId,
                            Text("binds"),
                            "routing",
                            EnvironmentRuntimeArtifactKinds.RoutingBinding,
                            binding.SourceEndpoint.ResourceId,
                            deployment.ServiceId,
                            binding.ReplicaGroupName,
                            deployment.RuntimeRevisionId);
                    }

                    if (!string.IsNullOrWhiteSpace(binding.LoadBalancerResourceId) &&
                        resourceNodeIds.TryGetValue(binding.LoadBalancerResourceId, out var loadBalancerNodeId))
                    {
                        AddLink(
                            links,
                            loadBalancerNodeId,
                            bindingNodeId,
                            Text("materializes"),
                            "routing",
                            EnvironmentRuntimeArtifactKinds.RoutingBinding,
                            binding.LoadBalancerResourceId,
                            serviceId,
                            binding.ReplicaGroupName,
                            deployment.RuntimeRevisionId);
                    }

                    if (replicaGroupNodeIds.TryGetValue(binding.ReplicaGroupName, out var replicaNodeId))
                    {
                        AddLink(
                            links,
                            bindingNodeId,
                            replicaNodeId,
                            Text("targets"),
                            "routing",
                            EnvironmentRuntimeArtifactKinds.RoutingBinding,
                            binding.SourceEndpoint.ResourceId,
                            deployment.ServiceId,
                            binding.ReplicaGroupName,
                            deployment.RuntimeRevisionId);
                    }
                }
            }

            foreach (var loadBalancer in resources.Where(resource => resource.ResourceLoadBalancerRoutes.Count > 0))
            {
                foreach (var route in loadBalancer.ResourceLoadBalancerRoutes)
                {
                    var routeNodeId = CreateNodeId("route", $"{loadBalancer.Id}:{route.Id}");
                    var endpoint = string.IsNullOrWhiteSpace(route.Target.EndpointName)
                        ? route.Target.Port?.ToString(CultureInfo.InvariantCulture)
                        : route.Target.EndpointName;
                    nodes[routeNodeId] = new EnvironmentRuntimeMapNode(
                        routeNodeId,
                        route.Name,
                        "Load-balancer route",
                        "load-balancer",
                        loadBalancer.EffectiveTypeId,
                        "routing",
                        $"{loadBalancer.EffectiveDisplayName} -> {route.Target.ResourceId}/{GetOptional(endpoint)}",
                        route.Kind.ToString(),
                        "state-running",
                        options.CreateResourceDetailUrl(loadBalancer),
                        EnvironmentRuntimeArtifactKinds.LoadBalancerRoute,
                        loadBalancer.Id,
                        null,
                        null,
                        null);
                    routingNodeCount++;

                    if (resourceNodeIds.TryGetValue(loadBalancer.Id, out var loadBalancerNodeId))
                    {
                        AddLink(
                            links,
                            loadBalancerNodeId,
                            routeNodeId,
                            Text("has route"),
                            "routing",
                            EnvironmentRuntimeArtifactKinds.LoadBalancerRoute,
                            loadBalancer.Id,
                            null,
                            null,
                            null);
                    }

                    if (resourceNodeIds.TryGetValue(route.Target.ResourceId, out var targetNodeId))
                    {
                        AddLink(
                            links,
                            routeNodeId,
                            targetNodeId,
                            Text("routes to"),
                            "routing",
                            EnvironmentRuntimeArtifactKinds.LoadBalancerRoute,
                            route.Target.ResourceId,
                            null,
                            null,
                            null);
                    }
                }
            }

            foreach (var mapping in resources.Where(ResourceNameMappingDisplay.IsNameMappingResource))
            {
                var mappingNodeId = CreateNodeId("name-mapping", mapping.Id);
                nodes[mappingNodeId] = new EnvironmentRuntimeMapNode(
                    mappingNodeId,
                    ResourceNameMappingDisplay.GetHostName(mapping),
                    "Name mapping",
                    "name-mapping",
                    mapping.EffectiveTypeId,
                    "routing",
                    $"{ResourceNameMappingDisplay.GetTargetResourceId(mapping)}/{ResourceNameMappingDisplay.GetTargetEndpointName(mapping)}",
                    ResourceNameMappingDisplay.GetMaterializationLabel(mapping, Text),
                    "state-running",
                    options.CreateResourceDetailUrl(mapping),
                    EnvironmentRuntimeArtifactKinds.EndpointMapping,
                    mapping.Id,
                    null,
                    null,
                    null);
                routingNodeCount++;

                if (!string.IsNullOrWhiteSpace(mapping.ParentResourceId) &&
                    resourceNodeIds.TryGetValue(mapping.ParentResourceId, out var zoneNodeId))
                {
                    AddLink(
                        links,
                        zoneNodeId,
                        mappingNodeId,
                        Text("contains"),
                        "routing",
                        EnvironmentRuntimeArtifactKinds.EndpointMapping,
                        mapping.ParentResourceId,
                        null,
                        null,
                        null);
                }

                if (resourceNodeIds.TryGetValue(mapping.Id, out var mappingResourceNodeId))
                {
                    AddLink(
                        links,
                        mappingResourceNodeId,
                        mappingNodeId,
                        Text("projects"),
                        "routing",
                        EnvironmentRuntimeArtifactKinds.EndpointMapping,
                        mapping.Id,
                        null,
                        null,
                        null);
                }

                if (resourceNodeIds.TryGetValue(ResourceNameMappingDisplay.GetTargetResourceId(mapping), out var targetNodeId))
                {
                    AddLink(
                        links,
                        mappingNodeId,
                        targetNodeId,
                        Text("names"),
                        "routing",
                        EnvironmentRuntimeArtifactKinds.EndpointMapping,
                        ResourceNameMappingDisplay.GetTargetResourceId(mapping),
                        null,
                        null,
                        null);
                }
            }

            if (options.IncludeNetworkTopologyOverlay)
            {
                AddNetworkTopologyNodes(
                    resources,
                    internetReachability,
                    nodes,
                    links,
                    resourceNodeIds,
                    ref networkTopologyNodeCount);
            }
        }

        private void AddNetworkTopologyNodes(
            IReadOnlyList<Resource> resources,
            IReadOnlyDictionary<string, string> internetReachability,
            IDictionary<string, EnvironmentRuntimeMapNode> nodes,
            IDictionary<string, EnvironmentRuntimeMapLink> links,
            IReadOnlyDictionary<string, string> resourceNodeIds,
            ref int networkTopologyNodeCount)
        {
            foreach (var network in resources.Where(resource => resource.ResourceEndpointMappings.Count > 0))
            {
                foreach (var mapping in network.ResourceEndpointMappings)
                {
                    var mappingNodeId = CreateNodeId("endpoint-mapping", $"{network.Id}:{mapping.Id}");
                    nodes[mappingNodeId] = new EnvironmentRuntimeMapNode(
                        mappingNodeId,
                        mapping.Name,
                        GetEndpointMappingText(mapping, resources),
                        "endpoint",
                        ResourceClass.Network.ToString(),
                        "topology",
                        $"{mapping.Source.ResourceId}/{mapping.Source.EndpointName} -> {mapping.Target.ResourceId}/{mapping.Target.EndpointName}",
                        Text("Configured"),
                        "state-running",
                        options.CreateResourceDetailUrl(network),
                        EnvironmentRuntimeArtifactKinds.EndpointMapping,
                        network.Id,
                        null,
                        null,
                        null);
                    networkTopologyNodeCount++;

                    var networkResourceId = FirstNonEmpty(mapping.NetworkResourceId, network.Id);
                    if (networkResourceId is not null &&
                        resourceNodeIds.TryGetValue(networkResourceId, out var networkNodeId))
                    {
                        AddLink(
                            links,
                            networkNodeId,
                            mappingNodeId,
                            Text("contains"),
                            "topology",
                            EnvironmentRuntimeArtifactKinds.EndpointMapping,
                            networkResourceId,
                            null,
                            null,
                            null);
                    }

                    if (resourceNodeIds.TryGetValue(mapping.Source.ResourceId, out var sourceNodeId))
                    {
                        AddLink(
                            links,
                            sourceNodeId,
                            mappingNodeId,
                            Text("publishes"),
                            "topology",
                            EnvironmentRuntimeArtifactKinds.EndpointMapping,
                            mapping.Source.ResourceId,
                            null,
                            null,
                            null);
                    }

                    if (!string.IsNullOrWhiteSpace(mapping.ProviderResourceId) &&
                        resourceNodeIds.TryGetValue(mapping.ProviderResourceId, out var providerNodeId))
                    {
                        AddLink(
                            links,
                            providerNodeId,
                            mappingNodeId,
                            Text("materializes"),
                            "topology",
                            EnvironmentRuntimeArtifactKinds.EndpointMapping,
                            mapping.ProviderResourceId,
                            null,
                            null,
                            null);
                    }

                    if (resourceNodeIds.TryGetValue(mapping.Target.ResourceId, out var targetNodeId))
                    {
                        AddLink(
                            links,
                            mappingNodeId,
                            targetNodeId,
                            Text("targets"),
                            "topology",
                            EnvironmentRuntimeArtifactKinds.EndpointMapping,
                            mapping.Target.ResourceId,
                            null,
                            null,
                            null);
                    }
                }
            }

            AddInternetConnectivityNode(
                resources,
                internetReachability,
                nodes,
                links,
                resourceNodeIds,
                ref networkTopologyNodeCount);
        }

        private void AddInternetConnectivityNode(
            IReadOnlyList<Resource> resources,
            IReadOnlyDictionary<string, string> internetReachability,
            IDictionary<string, EnvironmentRuntimeMapNode> nodes,
            IDictionary<string, EnvironmentRuntimeMapLink> links,
            IReadOnlyDictionary<string, string> resourceNodeIds,
            ref int networkTopologyNodeCount)
        {
            var carrierNodeIds = CreateInternetCarrierNodeIds(
                resources,
                internetReachability,
                nodes,
                resourceNodeIds);
            if (carrierNodeIds.Count == 0)
            {
                return;
            }

            var internetNodeId = CreateNodeId("internet", "public");
            nodes[internetNodeId] = new EnvironmentRuntimeMapNode(
                internetNodeId,
                Text("Internet"),
                "Internet connection",
                "web",
                ResourceClass.Network.ToString(),
                "topology",
                Text("Public reachability"),
                Text("Projected"),
                "state-running",
                null,
                EnvironmentRuntimeArtifactKinds.InternetConnection,
                null,
                null,
                null,
                null);
            networkTopologyNodeCount++;

            foreach (var carrier in carrierNodeIds)
            {
                AddLink(
                    links,
                    internetNodeId,
                    carrier.Value,
                    Text("reaches"),
                    "topology",
                    EnvironmentRuntimeArtifactKinds.InternetConnection,
                    carrier.Key,
                    null,
                    null,
                    null);
            }
        }

        private static IReadOnlyDictionary<string, string> CreateInternetCarrierNodeIds(
            IReadOnlyList<Resource> resources,
            IReadOnlyDictionary<string, string> internetReachability,
            IDictionary<string, EnvironmentRuntimeMapNode> nodes,
            IReadOnlyDictionary<string, string> resourceNodeIds)
        {
            var carriers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var hostNetworkNodeId = CreateNodeId("resource", ResourceInternetReachabilityProjection.HostNetworkResourceId);
            if (nodes.TryGetValue(hostNetworkNodeId, out var hostNetworkNode) &&
                !string.IsNullOrWhiteSpace(hostNetworkNode.InternetReachability))
            {
                carriers[ResourceInternetReachabilityProjection.HostNetworkResourceId] = hostNetworkNodeId;
            }

            foreach (var resource in resources)
            {
                if (!resourceNodeIds.TryGetValue(resource.Id, out var nodeId) ||
                    !internetReachability.ContainsKey(resource.Id))
                {
                    continue;
                }

                if (resource.ResourceClass == ResourceClass.Network ||
                    HasExplicitInternetReachability(resource))
                {
                    carriers[resource.Id] = nodeId;
                }
            }

            return carriers;
        }

        private static bool HasExplicitInternetReachability(Resource resource) =>
            !string.IsNullOrWhiteSpace(FirstNonEmpty(
                resource.ResourceAttributes.GetValueOrDefault(ResourceAttributeNames.InternetReachability),
                resource.ResourceAttributes.GetValueOrDefault(ResourceAttributeNames.NetworkInternetReachability)));

        private string GetEndpointMappingText(
            ResourceEndpointMappingDefinition mapping,
            IReadOnlyList<Resource> resources)
        {
            var target = resources.FirstOrDefault(resource =>
                string.Equals(resource.Id, mapping.Target.ResourceId, StringComparison.OrdinalIgnoreCase));
            if (target?.GetEndpointNetworkMapping(mapping.Target.EndpointName) is { } endpointMapping)
            {
                return FormatEndpointAddress(endpointMapping.Address);
            }

            return Text("Endpoint mapping");
        }

        private static string FormatEndpointAddress(string address) =>
            TryFormatEndpointHost(address) ?? address;

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

        private void AddInferredHostNetworkNode(
            IReadOnlyList<Resource> resources,
            IDictionary<string, EnvironmentRuntimeMapNode> nodes,
            IDictionary<string, EnvironmentRuntimeMapLink> links,
            IReadOnlyDictionary<string, string> resourceNodeIds,
            ref int networkTopologyNodeCount)
        {
            if (resourceNodeIds.ContainsKey(ResourceInternetReachabilityProjection.HostNetworkResourceId))
            {
                return;
            }

            var connectedResourceIds = ResourceInternetReachabilityProjection.GetHostNetworkConnectedResourceIds(resources)
                .Where(resourceNodeIds.ContainsKey)
                .ToArray();
            if (connectedResourceIds.Length == 0)
            {
                return;
            }

            var hostNetworkNodeId = CreateNodeId("resource", ResourceInternetReachabilityProjection.HostNetworkResourceId);
            nodes[hostNetworkNodeId] = new EnvironmentRuntimeMapNode(
                hostNetworkNodeId,
                Text("Host network"),
                "cloudshell.network",
                "network",
                ResourceClass.Network.ToString(),
                "topology",
                Text("Implicit host network for local development"),
                Text("Inferred"),
                "state-running",
                null,
                EnvironmentRuntimeArtifactKinds.Resource,
                ResourceInternetReachabilityProjection.HostNetworkResourceId,
                null,
                null,
                null,
                ResourceInternetReachabilityProjection.Inferred);
            networkTopologyNodeCount++;

            foreach (var resourceId in connectedResourceIds)
            {
                AddLink(
                    links,
                    hostNetworkNodeId,
                    resourceNodeIds[resourceId],
                    Text("connects"),
                    "topology",
                    EnvironmentRuntimeArtifactKinds.Resource,
                    resourceId,
                    null,
                    null,
                    null);
            }
        }

        private string Text(string value) => options.Localize(value);

        private string Text(string value, params object?[] args) =>
            options.Format(options.Localize(value), args);
    }

    private static bool ShouldShowEnvironmentMapResource(
        Resource resource,
        bool includeNetworkTopologyOverlay,
        IReadOnlySet<string> topologyResourceIds) =>
        !IsContainerApplicationResource(resource) &&
        (IsContainerReplicaResource(resource) ||
            resource.ManagementMode != ResourceManagementMode.UserManaged ||
            resource.State is not null ||
            resource.ResourceLoadBalancerRoutes.Count > 0 ||
            ResourceNameMappingDisplay.IsNameMappingResource(resource) ||
            includeNetworkTopologyOverlay && topologyResourceIds.Contains(resource.Id));

    private static HashSet<string> CreateTopologyResourceIds(
        IReadOnlyList<Resource> resources,
        IReadOnlyDictionary<string, string> internetReachability)
    {
        var resourceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var resource in resources)
        {
            if (resource.ResourceClass == ResourceClass.Network ||
                resource.ResourceEndpointMappings.Count > 0 ||
                resource.ResourceLoadBalancerRoutes.Count > 0 ||
                ResourceNameMappingDisplay.IsNameMappingResource(resource))
            {
                resourceIds.Add(resource.Id);
            }

            foreach (var mapping in resource.ResourceEndpointMappings)
            {
                AddTopologyResourceId(resourceIds, mapping.Source.ResourceId);
                AddTopologyResourceId(resourceIds, mapping.Target.ResourceId);
                AddTopologyResourceId(resourceIds, mapping.NetworkResourceId);
                AddTopologyResourceId(resourceIds, mapping.ProviderResourceId);
            }

            foreach (var mapping in resource.ResourceEndpointNetworkMappings)
            {
                AddTopologyResourceId(resourceIds, mapping.NetworkResourceId);
                AddTopologyResourceId(resourceIds, mapping.ProviderResourceId);
            }

            if (internetReachability.ContainsKey(resource.Id))
            {
                resourceIds.Add(resource.Id);
            }

            foreach (var route in resource.ResourceLoadBalancerRoutes)
            {
                AddTopologyResourceId(resourceIds, route.Target.ResourceId);
            }

            if (ResourceNameMappingDisplay.IsNameMappingResource(resource))
            {
                AddTopologyResourceId(resourceIds, ResourceNameMappingDisplay.GetTargetResourceId(resource));
            }
        }

        return resourceIds;
    }

    private static void AddTopologyResourceId(ISet<string> resourceIds, string? resourceId)
    {
        if (!string.IsNullOrWhiteSpace(resourceId))
        {
            resourceIds.Add(resourceId.Trim());
        }
    }

    private static bool IsContainerApplicationResource(Resource resource) =>
        string.Equals(resource.EffectiveTypeId, "application.container-app", StringComparison.OrdinalIgnoreCase) ||
        resource.ResourceAttributes.ContainsKey(ResourceAttributeNames.ContainerImage) &&
        resource.ResourceAttributes.ContainsKey(ResourceAttributeNames.ContainerReplicas);

    private static bool IsContainerReplicaResource(Resource resource) =>
        string.Equals(
            resource.ResourceAttributes.GetValueOrDefault(ResourceAttributeNames.RuntimeKind),
            "containerReplica",
            StringComparison.OrdinalIgnoreCase);

    private static string GetContainerApplicationServiceId(Resource resource) =>
        FirstProjected(
            resource.ResourceAttributes.GetValueOrDefault(ResourceAttributeNames.DeploymentServiceId),
            ResourceOrchestratorReplicaGroups.CreateDefaultServiceName(resource.Id));

    private static string FirstProjected(params string?[] values) =>
        values.First(value => IsProjected(value))!;

    private static string GetReplicaServiceId(
        Resource replica,
        string? owningResourceId,
        IReadOnlyDictionary<string, Resource> resourcesById)
    {
        var projectedServiceId = replica.ResourceAttributes.GetValueOrDefault(ResourceAttributeNames.DeploymentServiceId);
        if (IsProjected(projectedServiceId))
        {
            return projectedServiceId!;
        }

        if (owningResourceId is not null &&
            resourcesById.TryGetValue(owningResourceId, out var ownerResource))
        {
            return GetContainerApplicationServiceId(ownerResource);
        }

        return ResourceOrchestratorReplicaGroups.CreateDefaultServiceName(replica.Id);
    }

    private static string GetReplicaGroupId(
        Resource replica,
        string? owningResourceId,
        IReadOnlyDictionary<string, Resource> resourcesById,
        IReadOnlyDictionary<string, EnvironmentRuntimeMapReplicaGroup> activeReplicaGroupRows,
        string serviceId)
    {
        var projectedReplicaGroupId = replica.ResourceAttributes.GetValueOrDefault(ResourceAttributeNames.DeploymentReplicaGroupId);
        if (IsProjected(projectedReplicaGroupId))
        {
            return projectedReplicaGroupId!;
        }

        if (owningResourceId is not null &&
            resourcesById.TryGetValue(owningResourceId, out var ownerResource))
        {
            projectedReplicaGroupId = ownerResource.ResourceAttributes.GetValueOrDefault(ResourceAttributeNames.DeploymentReplicaGroupId);
            if (IsProjected(projectedReplicaGroupId))
            {
                return projectedReplicaGroupId!;
            }
        }

        var matchingReplicaGroup = activeReplicaGroupRows.Values.FirstOrDefault(replicaGroup =>
            string.Equals(replicaGroup.ServiceId, serviceId, StringComparison.OrdinalIgnoreCase));
        if (matchingReplicaGroup is not null)
        {
            return matchingReplicaGroup.ReplicaGroupId;
        }

        var runtimeRevisionId = FirstNonEmpty(
            replica.ResourceAttributes.GetValueOrDefault(ResourceAttributeNames.RuntimeRevision),
            owningResourceId is not null && resourcesById.TryGetValue(owningResourceId, out var owner)
                ? owner.ResourceAttributes.GetValueOrDefault(ResourceAttributeNames.DeploymentRevision)
                : null);
        return ResourceOrchestratorReplicaGroups.CreateReplicaGroupId(serviceId, runtimeRevisionId);
    }

    private static void AddResourceRelationshipLinks(
        IReadOnlyList<Resource> resources,
        IDictionary<string, EnvironmentRuntimeMapLink> links,
        IReadOnlyDictionary<string, string> resourceNodeIds,
        IReadOnlyDictionary<string, string> serviceNodeIds,
        bool includeDependencyRelationships)
    {
        var resourcesById = resources.ToDictionary(resource => resource.Id, StringComparer.OrdinalIgnoreCase);
        var resourceRelationshipNodeIds = CreateResourceRelationshipNodeIds(
            resources,
            resourceNodeIds,
            serviceNodeIds);

        foreach (var resource in resources)
        {
            if (includeDependencyRelationships)
            {
                foreach (var dependencyId in resource.DependsOn)
                {
                    if (resourceRelationshipNodeIds.TryGetValue(resource.Id, out var sourceNodeId) &&
                        resourceRelationshipNodeIds.TryGetValue(dependencyId, out var targetNodeId))
                    {
                        var label = TryGetDatabaseServerResourceId(resource, out var serverResourceId) &&
                            string.Equals(dependencyId, serverResourceId, StringComparison.OrdinalIgnoreCase)
                                ? "hosted by"
                                : "depends on";
                        AddLink(
                            links,
                            sourceNodeId,
                            targetNodeId,
                            label,
                            "dependency",
                            EnvironmentRuntimeArtifactKinds.Resource,
                            resource.Id,
                            null,
                            null,
                            null);
                    }
                }

                foreach (var dependencyId in resource.DependsOn.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!resourcesById.TryGetValue(dependencyId, out var dependency) ||
                        !TryGetDatabaseServerResourceId(dependency, out var serverResourceId) ||
                        resourceRelationshipNodeIds.ContainsKey(dependency.Id) ||
                        resource.DependsOn.Contains(serverResourceId, StringComparer.OrdinalIgnoreCase) ||
                        !resourceRelationshipNodeIds.TryGetValue(resource.Id, out var sourceNodeId) ||
                        !resourceRelationshipNodeIds.TryGetValue(serverResourceId, out var serverNodeId))
                    {
                        continue;
                    }

                    AddLink(
                        links,
                        sourceNodeId,
                        serverNodeId,
                        "uses database on",
                        "dependency",
                        EnvironmentRuntimeArtifactKinds.Resource,
                        resource.Id,
                        null,
                        null,
                        null);
                }
            }

            if (!string.IsNullOrWhiteSpace(resource.ParentResourceId) &&
                resourceNodeIds.TryGetValue(resource.ParentResourceId, out var parentNodeId) &&
                resourceNodeIds.TryGetValue(resource.Id, out var childNodeId))
            {
                AddLink(
                    links,
                    parentNodeId,
                    childNodeId,
                    "contains",
                    "relationship",
                    EnvironmentRuntimeArtifactKinds.Resource,
                    resource.ParentResourceId,
                    null,
                    null,
                    null);
            }
        }
    }

    private static Dictionary<string, string> CreateResourceRelationshipNodeIds(
        IReadOnlyList<Resource> resources,
        IReadOnlyDictionary<string, string> resourceNodeIds,
        IReadOnlyDictionary<string, string> serviceNodeIds)
    {
        var relationshipNodeIds = new Dictionary<string, string>(resourceNodeIds, StringComparer.OrdinalIgnoreCase);
        foreach (var resource in resources.Where(IsContainerApplicationResource))
        {
            var serviceId = GetContainerApplicationServiceId(resource);
            if (serviceNodeIds.TryGetValue(serviceId, out var serviceNodeId))
            {
                relationshipNodeIds[resource.Id] = serviceNodeId;
            }
        }

        return relationshipNodeIds;
    }

    private static void AddLink(
        IDictionary<string, EnvironmentRuntimeMapLink> links,
        string source,
        string target,
        string label,
        string kind,
        string? artifactKind,
        string? resourceId,
        string? serviceId,
        string? replicaGroupId,
        string? runtimeRevisionId,
        string? scope = null)
    {
        if (string.IsNullOrWhiteSpace(source) ||
            string.IsNullOrWhiteSpace(target) ||
            string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var id = string.Join('\u001f', source, label, target);
        links[id] = new EnvironmentRuntimeMapLink(
            source,
            target,
            label,
            kind,
            artifactKind,
            resourceId,
            serviceId,
            replicaGroupId,
            runtimeRevisionId,
            scope ?? GetDefaultLinkScope(kind));
    }

    private static string GetDefaultLinkScope(string kind) =>
        string.Equals(kind, "orchestration", StringComparison.OrdinalIgnoreCase)
            ? EnvironmentRuntimeMapLinkScopes.Internal
            : EnvironmentRuntimeMapLinkScopes.External;

    private static string CreateNodeId(string prefix, string value) =>
        $"{prefix}:{value.Trim()}";

    private static string CreateGroupId(string prefix, string value) =>
        $"group:{prefix}:{value.Trim()}";

    private static int GetNodeSortOrder(string nodeKind) =>
        nodeKind switch
        {
            "resource" => 0,
            "service" => 1,
            "replica-group" => 2,
            "replica" => 3,
            "routing" => 4,
            _ => 5
        };

    private static int GetGroupSortOrder(string kind) =>
        kind switch
        {
            "service" => 0,
            "replica-group" => 1,
            _ => 2
        };

    private static bool IsProjected(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !string.Equals(value, "not projected", StringComparison.OrdinalIgnoreCase);

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string? GetAttribute(Resource resource, string name) =>
        resource.ResourceAttributes.GetValueOrDefault(name);

    private static string GetOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "not projected" : value;

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

}

public sealed record EnvironmentRuntimeMap(
    IReadOnlyList<EnvironmentRuntimeMapNode> Nodes,
    IReadOnlyList<EnvironmentRuntimeMapGroup> Groups,
    IReadOnlyList<EnvironmentRuntimeMapLink> Links,
    int ResourceCount,
    int ServiceCount,
    int RoutingCount,
    int NetworkTopologyCount)
{
    public static EnvironmentRuntimeMap Empty { get; } = new([], [], [], 0, 0, 0, 0);
}

public sealed record EnvironmentRuntimeMapNode(
    string Id,
    string Label,
    string Type,
    string? IconName,
    string ResourceClass,
    string NodeKind,
    string Summary,
    string StateLabel,
    string StateClass,
    string? DetailUrl,
    string ArtifactKind,
    string? ResourceId,
    string? ServiceId,
    string? ReplicaGroupId,
    string? RuntimeRevisionId,
    string? InternetReachability = null,
    string? EndpointText = null);

public sealed record EnvironmentRuntimeMapGroup(
    string Id,
    string Label,
    string Kind,
    string? ParentGroupId,
    string? BadgeLabel,
    string? DetailUrl,
    string ArtifactKind,
    string? ResourceId,
    string? ServiceId,
    string? ReplicaGroupId,
    string? RuntimeRevisionId,
    IReadOnlyList<string> NodeIds);

public sealed record EnvironmentRuntimeMapLink(
    string Source,
    string Target,
    string Label,
    string Kind,
    string? ArtifactKind,
    string? ResourceId,
    string? ServiceId,
    string? ReplicaGroupId,
    string? RuntimeRevisionId,
    string Scope);

public sealed record EnvironmentRuntimeMapReplicaGroup(
    string SourceResourceId,
    string ServiceId,
    string ReplicaGroupId,
    string? RuntimeRevisionId,
    int RequestedSlots,
    int OccupiedSlots,
    int MaterializedReplicas,
    int RepairingCount,
    int RepairFailedCount);

public sealed record EnvironmentRuntimeMapProjectionOptions
{
    public bool IncludeNetworkTopologyOverlay { get; init; }

    public bool IncludeDependencyRelationships { get; init; }

    public Func<string, string> Localize { get; init; } = static value => value;

    public Func<string, object?[], string> Format { get; init; } =
        static (format, args) => string.Format(CultureInfo.CurrentCulture, format, args);

    public Func<Resource, string?> CreateResourceDetailUrl { get; init; } = static _ => null;

    public Func<ResourceState?, string> GetStateClass { get; init; } = static _ => "state-unknown";

    public Func<Resource, string> GetResourceTypeLabel { get; init; } = static resource => resource.EffectiveTypeId;

    public Func<Resource, string?> GetResourceIconName { get; init; } = static resource => resource.EffectiveTypeId;
}

public static class EnvironmentRuntimeArtifactKinds
{
    public const string Resource = "resource";
    public const string OrchestrationService = "orchestration-service";
    public const string ReplicaGroup = "replica-group";
    public const string Replica = "replica";
    public const string RoutingBinding = "routing-binding";
    public const string LoadBalancerRoute = "load-balancer-route";
    public const string EndpointMapping = "endpoint-mapping";
    public const string InternetConnection = "internet-connection";
    public const string Deployment = "deployment";
}

public static class EnvironmentRuntimeMapLinkScopes
{
    public const string External = "external";
    public const string Internal = "internal";
}

internal sealed class EnvironmentRuntimeMapGroupBuilder(
    string id,
    string label,
    string kind,
    string? parentGroupId)
{
    public string Id { get; } = id;

    public string Label { get; } = label;

    public string Kind { get; } = kind;

    public string? ParentGroupId { get; } = parentGroupId;

    public string? BadgeLabel { get; private set; }

    public string? DetailUrl { get; set; }

    public string ArtifactKind { get; set; } = "group";

    public string? ResourceId { get; set; }

    public string? ServiceId { get; set; }

    public string? ReplicaGroupId { get; set; }

    public string? RuntimeRevisionId { get; set; }

    public HashSet<string> NodeIds { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void SetBadgeLabel(string? badgeLabel)
    {
        if (!string.IsNullOrWhiteSpace(badgeLabel))
        {
            BadgeLabel = badgeLabel;
        }
    }

    public EnvironmentRuntimeMapGroup ToModel() =>
        new(
            Id,
            Label,
            Kind,
            ParentGroupId,
            BadgeLabel,
            DetailUrl,
            ArtifactKind,
            ResourceId,
            ServiceId,
            ReplicaGroupId,
            RuntimeRevisionId,
            NodeIds.OrderBy(nodeId => nodeId, StringComparer.OrdinalIgnoreCase).ToArray());
}
