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
            var routingNodeCount = 0;

            foreach (var resource in resources
                .Where(ShouldShowEnvironmentMapResource)
                .OrderBy(resource => resource.EffectiveDisplayName, StringComparer.CurrentCultureIgnoreCase))
            {
                var nodeId = CreateNodeId("resource", resource.Id);
                resourceNodeIds[resource.Id] = nodeId;
                nodes[nodeId] = new EnvironmentRuntimeMapNode(
                    nodeId,
                    resource.EffectiveDisplayName,
                    resource.EffectiveTypeId,
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
                    GetAttribute(resource, ResourceAttributeNames.RuntimeRevision));
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

            AddResourceRelationshipLinks(resources, links, resourceNodeIds);
            AddRoutingNodes(resources, deployments, nodes, links, resourceNodeIds, replicaGroupNodeIds, ref routingNodeCount);

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
                routingNodeCount);
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
            IDictionary<string, EnvironmentRuntimeMapNode> nodes,
            IDictionary<string, EnvironmentRuntimeMapLink> links,
            IReadOnlyDictionary<string, string> resourceNodeIds,
            IReadOnlyDictionary<string, string> replicaGroupNodeIds,
            ref int routingNodeCount)
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
        }

        private string Text(string value) => options.Localize(value);

        private string Text(string value, params object?[] args) =>
            options.Format(options.Localize(value), args);
    }

    private static bool ShouldShowEnvironmentMapResource(Resource resource) =>
        !IsContainerApplicationResource(resource) &&
        (IsContainerReplicaResource(resource) ||
            resource.ManagementMode != ResourceManagementMode.UserManaged ||
            resource.State is not null ||
            resource.ResourceLoadBalancerRoutes.Count > 0 ||
            ResourceNameMappingDisplay.IsNameMappingResource(resource));

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
        IReadOnlyDictionary<string, string> resourceNodeIds)
    {
        foreach (var resource in resources)
        {
            foreach (var dependencyId in resource.DependsOn)
            {
                if (resourceNodeIds.TryGetValue(resource.Id, out var sourceNodeId) &&
                    resourceNodeIds.TryGetValue(dependencyId, out var targetNodeId))
                {
                    AddLink(
                        links,
                        sourceNodeId,
                        targetNodeId,
                        "depends on",
                        "dependency",
                        EnvironmentRuntimeArtifactKinds.Resource,
                        resource.Id,
                        null,
                        null,
                        null);
                }
            }
        }
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
        string? runtimeRevisionId)
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
            runtimeRevisionId);
    }

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
}

public sealed record EnvironmentRuntimeMap(
    IReadOnlyList<EnvironmentRuntimeMapNode> Nodes,
    IReadOnlyList<EnvironmentRuntimeMapGroup> Groups,
    IReadOnlyList<EnvironmentRuntimeMapLink> Links,
    int ResourceCount,
    int ServiceCount,
    int RoutingCount)
{
    public static EnvironmentRuntimeMap Empty { get; } = new([], [], [], 0, 0, 0);
}

public sealed record EnvironmentRuntimeMapNode(
    string Id,
    string Label,
    string Type,
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
    string? RuntimeRevisionId);

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
    string? RuntimeRevisionId);

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
    public Func<string, string> Localize { get; init; } = static value => value;

    public Func<string, object?[], string> Format { get; init; } =
        static (format, args) => string.Format(CultureInfo.CurrentCulture, format, args);

    public Func<Resource, string?> CreateResourceDetailUrl { get; init; } = static _ => null;

    public Func<ResourceState?, string> GetStateClass { get; init; } = static _ => "state-unknown";
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
    public const string Deployment = "deployment";
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
