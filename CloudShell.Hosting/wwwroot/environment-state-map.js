import './d3.v7.min.js';

let graph = null;

export function initializeEnvironmentStateMap(selector, environmentInterop) {
    disposeEnvironmentStateMap();
    graph = new EnvironmentStateMap(selector, environmentInterop);
}

export function updateEnvironmentStateMap(map) {
    if (graph) {
        graph.update(map || { nodes: [], links: [] });
    }
}

export function disposeEnvironmentStateMap() {
    if (graph) {
        graph.dispose();
        graph = null;
    }
}

class EnvironmentStateMap {
    constructor(selector, environmentInterop) {
        this.environmentInterop = environmentInterop;
        this.map = { nodes: [], groups: [], links: [] };
        this.nodes = [];
        this.groups = [];
        this.links = [];
        this.selectedNode = null;
        this.svg = d3.select(selector);
        this.svg.selectAll("*").remove();
        this.baseGroup = this.svg.append("g").attr("class", "environment-map-stage");
        this.groupGroup = this.baseGroup.append("g").attr("class", "environment-map-groups");
        this.linkGroup = this.baseGroup.append("g").attr("class", "environment-map-links");
        this.linkLabelGroup = this.baseGroup.append("g").attr("class", "environment-map-link-labels");
        this.nodeGroup = this.baseGroup.append("g").attr("class", "environment-map-nodes");

        const defs = this.svg.append("defs");
        defs.append("marker")
            .attr("id", "environment-map-arrow")
            .attr("viewBox", "0 -5 10 10")
            .attr("refX", 98)
            .attr("refY", 0)
            .attr("markerWidth", 9)
            .attr("markerHeight", 9)
            .attr("orient", "auto")
            .append("path")
            .attr("d", "M0,-5L10,0L0,5")
            .attr("class", "environment-map-arrow");

        this.zoom = d3.zoom()
            .scaleExtent([0.25, 4])
            .on("zoom", event => {
                this.baseGroup.attr("transform", event.transform);
            });
        this.svg.call(this.zoom);

        this.linkForce = d3.forceLink()
            .id(node => node.id)
            .strength(link => link.scope === "internal" ? 0.88 : 0.62)
            .distance(link => link.scope === "internal" ? 155 : 230);

        this.simulation = d3.forceSimulation()
            .force("link", this.linkForce)
            .force("charge", d3.forceManyBody().strength(node => node.nodeKind === "service" ? -1350 : -920))
            .force("collide", d3.forceCollide(node => getNodeRadius(node) + 22).iterations(8))
            .force("x", d3.forceX(node => Number.isFinite(node.targetX) ? node.targetX : getLaneX(node.nodeKind)).strength(0.36))
            .force("y", d3.forceY(node => Number.isFinite(node.targetY) ? node.targetY : 0).strength(0.24))
            .force("center", d3.forceCenter().strength(0.015))
            .on("tick", () => this.onTick());

        this.drag = d3.drag()
            .on("start", event => {
                if (!event.active) {
                    this.simulation.alphaTarget(0.15).restart();
                }
                event.subject.fx = event.subject.x;
                event.subject.fy = event.subject.y;
            })
            .on("drag", event => {
                event.subject.fx = event.x;
                event.subject.fy = event.y;
            })
            .on("end", event => {
                if (!event.active) {
                    this.simulation.alphaTarget(0);
                }
                event.subject.fx = null;
                event.subject.fy = null;
            });

        this.registerControls();
        this.resizeObserver = new ResizeObserver(() => this.resize());
        const container = this.svg.node()?.closest(".environment-state-map-shell");
        if (container) {
            this.resizeObserver.observe(container);
        }
        this.resize();
    }

    registerControls() {
        d3.select(".environment-map-zoom-in").on("click.environmentMap", () => this.zoomBy(1.35));
        d3.select(".environment-map-zoom-out").on("click.environmentMap", () => this.zoomBy(1 / 1.35));
        d3.select(".environment-map-reset").on("click.environmentMap", () => this.resetZoom());
    }

    zoomBy(scale) {
        this.svg.transition().duration(160).call(this.zoom.scaleBy, scale);
    }

    resetZoom() {
        this.svg.transition().duration(180).call(this.zoom.transform, d3.zoomIdentity);
    }

    resize() {
        const element = this.svg.node();
        const container = element?.closest(".environment-state-map-shell");
        if (!element || !container) {
            return;
        }

        const width = Math.max(container.clientWidth, 360);
        const height = Math.max(container.clientHeight, 430);
        this.svg.attr("viewBox", `${-width / 2} ${-height / 2} ${width} ${height}`);
    }

    update(map) {
        const nodes = map.nodes || [];
        const groups = map.groups || [];
        const links = map.links || [];
        const changed = this.hasStructureChanged(nodes, groups, links);
        const previousNodes = new Map(this.nodes.map(node => [node.id, node]));
        const degreeMap = this.getDegrees(nodes, links);

        this.map = map;
        this.nodes = nodes.map(node => {
            const existing = previousNodes.get(node.id);
            return {
                ...existing,
                id: node.id,
                label: node.label,
                type: node.type,
                resourceClass: node.resourceClass,
                nodeKind: node.nodeKind,
                summary: node.summary,
                stateLabel: node.stateLabel,
                stateClass: node.stateClass,
                detailUrl: node.detailUrl,
                artifactKind: node.artifactKind,
                resourceId: node.resourceId,
                serviceId: node.serviceId,
                replicaGroupId: node.replicaGroupId,
                runtimeRevisionId: node.runtimeRevisionId,
                internetReachability: node.internetReachability,
                degree: degreeMap.get(node.id) || 1
            };
        });
        const visibleIds = new Set(this.nodes.map(node => node.id));
        this.groups = groups
            .map(group => ({
                id: group.id,
                label: group.label,
                kind: group.kind,
                parentGroupId: group.parentGroupId,
                badgeLabel: group.badgeLabel,
                detailUrl: group.detailUrl,
                artifactKind: group.artifactKind,
                resourceId: group.resourceId,
                serviceId: group.serviceId,
                replicaGroupId: group.replicaGroupId,
                runtimeRevisionId: group.runtimeRevisionId,
                nodeIds: group.nodeIds || []
            }))
            .filter(group => group.nodeIds.some(nodeId => visibleIds.has(nodeId)));
        this.links = links
            .filter(link => visibleIds.has(link.source) && visibleIds.has(link.target))
            .map(link => ({
                id: `${link.source}->${link.label}->${link.target}`,
                source: link.source,
                target: link.target,
                label: link.label,
                kind: link.kind,
                artifactKind: link.artifactKind,
                resourceId: link.resourceId,
                serviceId: link.serviceId,
                replicaGroupId: link.replicaGroupId,
                runtimeRevisionId: link.runtimeRevisionId,
                scope: link.scope || "external"
            }));

        this.applyLayoutTargets(changed);

        this.renderGroups();
        this.renderLinks();
        this.renderNodes();

        this.simulation.nodes(this.nodes);
        this.linkForce.links(this.links);

        if (changed) {
            this.simulation.stop();
            this.simulation.alpha(1);
            for (let i = 0; i < 240; i++) {
                this.simulation.tick();
            }
        }

        this.simulation.alpha(0.55).restart();
        this.updateHighlights();
    }

    hasStructureChanged(nodes, groups, links) {
        if (nodes.length !== (this.map.nodes || []).length ||
            groups.length !== (this.map.groups || []).length ||
            links.length !== (this.map.links || []).length) {
            return true;
        }

        const oldIds = new Set((this.map.nodes || []).map(node => node.id));
        if (nodes.some(node => !oldIds.has(node.id))) {
            return true;
        }

        const oldGroups = new Set((this.map.groups || []).map(group =>
            `${group.id}:${group.badgeLabel || ""}:${(group.nodeIds || []).join(",")}`));
        if (groups.some(group => !oldGroups.has(`${group.id}:${group.badgeLabel || ""}:${(group.nodeIds || []).join(",")}`))) {
            return true;
        }

        const oldLinks = new Set((this.map.links || []).map(link =>
            `${link.source}->${link.label}->${link.target}`));
        return links.some(link => !oldLinks.has(`${link.source}->${link.label}->${link.target}`));
    }

    applyLayoutTargets(resetPositions) {
        const nodeMap = new Map(this.nodes.map(node => [node.id, node]));
        const serviceGroups = this.groups
            .filter(group => group.kind === "service")
            .sort(compareLabels);
        const assigned = new Set();
        const serviceYById = new Map();
        const serviceSpacing = 390;
        const firstServiceY = -((serviceGroups.length - 1) * serviceSpacing) / 2;

        serviceGroups.forEach((group, serviceIndex) => {
            const baseY = firstServiceY + serviceIndex * serviceSpacing;
            const memberNodes = [...new Set(group.nodeIds)]
                .map(nodeId => nodeMap.get(nodeId))
                .filter(Boolean)
                .sort(compareLabels);

            if (group.serviceId) {
                serviceYById.set(group.serviceId, baseY);
            }

            const serviceNode = memberNodes.find(node => node.nodeKind === "service") ||
                this.nodes.find(node => node.nodeKind === "service" && node.serviceId === group.serviceId);
            if (serviceNode) {
                setLayoutTarget(serviceNode, -285, baseY, resetPositions);
                assigned.add(serviceNode.id);
            }

            const replicaGroupNodes = memberNodes
                .filter(node => node.nodeKind === "replica-group")
                .sort(compareLabels);
            const replicaGroupCount = Math.max(replicaGroupNodes.length, 1);
            replicaGroupNodes.forEach((replicaGroupNode, replicaGroupIndex) => {
                const groupY = baseY + (replicaGroupIndex - (replicaGroupCount - 1) / 2) * 215;
                setLayoutTarget(replicaGroupNode, 5, groupY, resetPositions);
                assigned.add(replicaGroupNode.id);

                const replicaNodes = this.nodes
                    .filter(node =>
                        node.nodeKind === "replica" &&
                        node.replicaGroupId &&
                        node.replicaGroupId === replicaGroupNode.replicaGroupId)
                    .sort(compareLabels);
                const rowCount = Math.min(replicaNodes.length, 4);
                replicaNodes.forEach((replicaNode, replicaIndex) => {
                    const row = rowCount === 0 ? 0 : replicaIndex % 4;
                    const column = Math.floor(replicaIndex / 4);
                    const visibleRows = Math.min(replicaNodes.length - column * 4, 4);
                    setLayoutTarget(
                        replicaNode,
                        300 + column * 215,
                        groupY + (row - (visibleRows - 1) / 2) * 132,
                        resetPositions);
                    assigned.add(replicaNode.id);
                });
            });
        });

        const routingNodes = this.nodes
            .filter(node => node.nodeKind === "routing" && !assigned.has(node.id))
            .sort(compareLabels);
        const routingByService = d3.group(routingNodes, node => node.serviceId || "");
        routingByService.forEach((nodes, serviceId) => {
            const baseY = serviceYById.get(serviceId) ?? getStackBaseY(nodes.length);
            nodes.forEach((node, index) => {
                setLayoutTarget(node, 560, baseY + getStackOffset(index, nodes.length, 126), resetPositions);
                assigned.add(node.id);
            });
        });

        const topologyNodes = this.nodes
            .filter(node => node.nodeKind === "topology" && !assigned.has(node.id))
            .sort(compareLabels);
        topologyNodes.forEach((node, index) => {
            setLayoutTarget(node, 760, getStackOffset(index, topologyNodes.length, 138), resetPositions);
            assigned.add(node.id);
        });

        const resourceNodes = this.nodes
            .filter(node => node.nodeKind === "resource" && !assigned.has(node.id))
            .sort(compareLabels);
        resourceNodes.forEach((node, index) => {
            setLayoutTarget(node, -560, getStackOffset(index, resourceNodes.length, 150), resetPositions);
            assigned.add(node.id);
        });

        const remainingNodes = this.nodes
            .filter(node => !assigned.has(node.id))
            .sort(compareLabels);
        remainingNodes.forEach((node, index) => {
            setLayoutTarget(
                node,
                getLaneX(node.nodeKind),
                getStackOffset(index, remainingNodes.length, 150),
                resetPositions);
        });
    }

    getDegrees(nodes, links) {
        const degrees = new Map(nodes.map(node => [node.id, 0]));
        links.forEach(link => {
            degrees.set(link.source, (degrees.get(link.source) || 0) + 1);
            degrees.set(link.target, (degrees.get(link.target) || 0) + 1);
        });
        return degrees;
    }

    renderGroups() {
        this.groupElements = this.groupGroup
            .selectAll(".environment-map-group")
            .data(this.groups, group => group.id);

        this.groupElements.exit()
            .transition()
            .duration(140)
            .attr("opacity", 0)
            .remove();

        const newGroups = this.groupElements.enter()
            .append("g")
            .attr("class", "environment-map-group")
            .attr("opacity", 0);

        newGroups.append("rect")
            .attr("class", "environment-map-group-boundary")
            .attr("rx", group => group.kind === "service" ? 10 : 7);

        newGroups.append("text")
            .attr("class", "environment-map-group-label");

        const resourceCards = newGroups.append("g")
            .attr("class", "environment-map-group-resource-card");

        resourceCards.append("rect")
            .attr("class", "environment-map-group-resource-card-frame");

        resourceCards.append("text")
            .attr("class", "environment-map-group-resource-card-title");

        resourceCards.append("text")
            .attr("class", "environment-map-group-resource-card-kind");

        newGroups.transition()
            .duration(140)
            .attr("opacity", 1);

        this.groupElements = newGroups.merge(this.groupElements)
            .attr("class", group => `environment-map-group ${getClassName(group.kind)}`);
        this.groupElements.select(".environment-map-group-label")
            .text(group => trimText(group.label, group.kind === "service" ? 34 : 28));
        this.groupElements.select(".environment-map-group-resource-card-title")
            .text(group => trimText(group.badgeLabel, 18));
        this.groupElements.select(".environment-map-group-resource-card-kind")
            .text(group => group.badgeLabel ? "managed resource" : "");
    }

    renderLinks() {
        this.linkElements = this.linkGroup
            .selectAll("line")
            .data(this.links, link => link.id);

        this.linkElements.exit()
            .transition()
            .duration(140)
            .attr("opacity", 0)
            .remove();

        const newLinks = this.linkElements.enter()
            .append("line")
            .attr("class", link => `environment-map-link ${getClassName(link.kind)} ${getClassName(link.scope)}`)
            .attr("opacity", 0);

        newLinks.transition()
            .duration(140)
            .attr("opacity", 1);

        this.linkElements = newLinks.merge(this.linkElements)
            .attr("class", link => `environment-map-link ${getClassName(link.kind)} ${getClassName(link.scope)}`);

        this.linkLabelElements = this.linkLabelGroup
            .selectAll("text")
            .data(this.links, link => link.id);

        this.linkLabelElements.exit()
            .transition()
            .duration(140)
            .attr("opacity", 0)
            .remove();

        const newLabels = this.linkLabelElements.enter()
            .append("text")
            .attr("class", "environment-map-link-label")
            .attr("opacity", 0);

        newLabels.transition()
            .duration(140)
            .attr("opacity", 1);

        this.linkLabelElements = newLabels.merge(this.linkLabelElements)
            .attr("class", link => `environment-map-link-label ${getClassName(link.kind)} ${getClassName(link.scope)}`)
            .text(link => trimText(link.label, 16));
    }

    renderNodes() {
        this.nodeElements = this.nodeGroup
            .selectAll(".environment-map-node")
            .data(this.nodes, node => node.id);

        this.nodeElements.exit()
            .transition()
            .duration(140)
            .attr("opacity", 0)
            .remove();

        const newNodes = this.nodeElements.enter()
            .append("g")
            .attr("class", "environment-map-node")
            .attr("opacity", 0)
            .call(this.drag)
            .on("click", (_event, node) => this.selectNode(node))
            .on("dblclick", (_event, node) => this.openNode(node))
            .on("mouseover", (_event, node) => {
                this.hoveredNode = node;
                this.updateHighlights();
            })
            .on("mouseout", () => {
                this.hoveredNode = null;
                this.updateHighlights();
            });

        newNodes.append("rect")
            .attr("class", "environment-map-node-card")
            .attr("x", -90)
            .attr("y", -52)
            .attr("width", 180)
            .attr("height", 104)
            .attr("rx", 6);

        newNodes.append("circle")
            .attr("class", "environment-map-node-icon")
            .attr("cx", -59)
            .attr("cy", -18)
            .attr("r", 20);

        newNodes.append("text")
            .attr("class", "environment-map-node-initials")
            .attr("x", -59)
            .attr("y", -13);

        newNodes.append("circle")
            .attr("class", "environment-map-status")
            .attr("cx", 72)
            .attr("cy", -37)
            .attr("r", 9)
            .append("title");

        const internetBadge = newNodes.append("g")
            .attr("class", "environment-map-internet-badge");

        internetBadge.append("circle")
            .attr("class", "environment-map-internet-badge-frame")
            .attr("cx", 46)
            .attr("cy", -37)
            .attr("r", 9);

        internetBadge.append("text")
            .attr("class", "environment-map-internet-badge-icon")
            .attr("x", 46)
            .attr("y", -33);

        internetBadge.append("title");

        newNodes.append("text")
            .attr("class", "environment-map-node-label")
            .attr("x", 0)
            .attr("y", 13);

        newNodes.append("text")
            .attr("class", "environment-map-node-kind")
            .attr("x", 0)
            .attr("y", 31);

        newNodes.append("title")
            .attr("class", "environment-map-node-title");

        newNodes.transition()
            .duration(140)
            .attr("opacity", 1);

        this.nodeElements = newNodes.merge(this.nodeElements);
        this.nodeElements
            .attr("class", node => `environment-map-node ${getClassName(node.nodeKind)}`);
        this.nodeElements.select(".environment-map-node-icon")
            .attr("class", node => `environment-map-node-icon ${getClassName(node.nodeKind)} ${getClassName(node.resourceClass)}`);
        this.nodeElements.select(".environment-map-node-initials")
            .text(node => getInitials(node.label));
        this.nodeElements.select(".environment-map-status")
            .attr("class", node => `environment-map-status ${node.stateClass || "state-unknown"}`)
            .select("title")
            .text(node => node.stateLabel);
        this.nodeElements.select(".environment-map-internet-badge")
            .attr("display", node => node.internetReachability ? null : "none")
            .attr("class", node => `environment-map-internet-badge ${getClassName(node.internetReachability)}`);
        this.nodeElements.select(".environment-map-internet-badge-icon")
            .text("↗");
        this.nodeElements.select(".environment-map-internet-badge title")
            .text(formatInternetReachabilityTitle);
        this.nodeElements.select(".environment-map-node-label")
            .text(node => trimText(node.label, 25));
        this.nodeElements.select(".environment-map-node-kind")
            .text(node => trimText(node.type, 28));
        this.nodeElements.select(".environment-map-node-title")
            .text(formatNodeTitle);
    }

    selectNode(node) {
        this.selectedNode = this.selectedNode?.id === node.id ? null : node;
        this.updateHighlights();
    }

    openNode(node) {
        if (node.detailUrl) {
            this.environmentInterop.invokeMethodAsync("OpenEnvironmentMapNode", node.detailUrl);
        }
    }

    updateHighlights() {
        const activeNode = this.hoveredNode || this.selectedNode;
        const neighborIds = activeNode ? new Set(this.getNeighborIds(activeNode)) : null;

        this.nodeElements
            ?.classed("selected", node => this.selectedNode?.id === node.id)
            .classed("related", node => neighborIds?.has(node.id) === true)
            .classed("dimmed", node => neighborIds !== null && !neighborIds.has(node.id));

        this.linkElements
            ?.classed("related", link => activeNode && this.isNeighborLink(activeNode, link))
            .classed("dimmed", link => activeNode && !this.isNeighborLink(activeNode, link));

        this.linkLabelElements
            ?.classed("related", link => activeNode && this.isNeighborLink(activeNode, link))
            .classed("dimmed", link => activeNode && !this.isNeighborLink(activeNode, link));

        this.groupElements
            ?.classed("related", group => activeNode && group.nodeIds.includes(activeNode.id))
            .classed("dimmed", group => activeNode && !group.nodeIds.includes(activeNode.id));
    }

    getNeighborIds(node) {
        const neighbors = [node.id];
        this.links.forEach(link => {
            const sourceId = getNodeId(link.source);
            const targetId = getNodeId(link.target);
            if (sourceId === node.id) {
                neighbors.push(targetId);
            } else if (targetId === node.id) {
                neighbors.push(sourceId);
            }
        });
        return neighbors;
    }

    isNeighborLink(node, link) {
        return getNodeId(link.source) === node.id || getNodeId(link.target) === node.id;
    }

    onTick() {
        this.updateGroupBounds();
        this.nodeElements?.attr("transform", node => `translate(${node.x},${node.y})`);
        this.linkElements
            ?.attr("x1", link => link.source.x)
            .attr("y1", link => link.source.y)
            .attr("x2", link => link.target.x)
            .attr("y2", link => link.target.y);
        this.linkLabelElements
            ?.attr("x", link => (link.source.x + link.target.x) / 2)
            .attr("y", link => (link.source.y + link.target.y) / 2 - 6);
    }

    updateGroupBounds() {
        const nodeMap = new Map(this.nodes.map(node => [node.id, node]));
        this.groupElements?.each(function (group) {
            const memberNodes = group.nodeIds
                .map(nodeId => nodeMap.get(nodeId))
                .filter(node => Number.isFinite(node?.x) && Number.isFinite(node?.y));
            if (memberNodes.length === 0) {
                d3.select(this).attr("display", "none");
                return;
            }

            const paddingX = group.kind === "service" ? 68 : 38;
            const paddingY = group.kind === "service" ? 58 : 38;
            const minX = d3.min(memberNodes, node => node.x - 98) - paddingX;
            const maxX = d3.max(memberNodes, node => node.x + 98) + paddingX;
            const minY = d3.min(memberNodes, node => node.y - 60) - paddingY;
            const maxY = d3.max(memberNodes, node => node.y + 60) + paddingY;
            const width = Math.max(maxX - minX, group.kind === "service" ? 320 : 230);
            const height = Math.max(maxY - minY, group.kind === "service" ? 230 : 160);

            const groupElement = d3.select(this).attr("display", null);
            groupElement.select(".environment-map-group-boundary")
                .attr("x", minX)
                .attr("y", minY)
                .attr("width", width)
                .attr("height", height);
            groupElement.select(".environment-map-group-label")
                .attr("x", minX + 18)
                .attr("y", minY + 24);
            const resourceCard = groupElement.select(".environment-map-group-resource-card")
                .attr("display", group.badgeLabel ? null : "none");
            const cardWidth = Math.max(126, String(group.badgeLabel || "").length * 7 + 34);
            resourceCard.select(".environment-map-group-resource-card-frame")
                .attr("x", maxX - cardWidth - 16)
                .attr("y", minY + 12)
                .attr("width", cardWidth)
                .attr("height", 42)
                .attr("rx", 6);
            resourceCard.select(".environment-map-group-resource-card-title")
                .attr("x", maxX - cardWidth + 2)
                .attr("y", minY + 29);
            resourceCard.select(".environment-map-group-resource-card-kind")
                .attr("x", maxX - cardWidth + 2)
                .attr("y", minY + 45);
        });
    }

    dispose() {
        this.simulation.stop();
        this.resizeObserver?.disconnect();
        d3.select(".environment-map-zoom-in").on("click.environmentMap", null);
        d3.select(".environment-map-zoom-out").on("click.environmentMap", null);
        d3.select(".environment-map-reset").on("click.environmentMap", null);
        this.svg.on(".zoom", null);
        this.svg.selectAll("*").remove();
    }
}

function getLaneX(nodeKind) {
    switch (nodeKind) {
        case "resource":
            return -560;
        case "service":
            return -285;
        case "replica-group":
            return 5;
        case "replica":
            return 300;
        case "routing":
            return 560;
        case "topology":
            return 760;
        default:
            return 0;
    }
}

function setLayoutTarget(node, x, y, resetPosition) {
    node.targetX = x;
    node.targetY = y;
    if (resetPosition || !Number.isFinite(node.x) || !Number.isFinite(node.y)) {
        node.x = x;
        node.y = y;
        node.vx = 0;
        node.vy = 0;
    }
}

function getStackBaseY(count) {
    return -((count - 1) * 150) / 2;
}

function getStackOffset(index, count, spacing) {
    return (index - (count - 1) / 2) * spacing;
}

function compareLabels(left, right) {
    return String(left?.label || left?.id || "").localeCompare(
        String(right?.label || right?.id || ""),
        undefined,
        { sensitivity: "base" });
}

function formatNodeTitle(node) {
    return [
        node.label,
        node.type,
        node.summary,
        `State: ${node.stateLabel}`,
        node.artifactKind ? `Artifact: ${node.artifactKind}` : "",
        node.resourceId ? `Resource: ${node.resourceId}` : "",
        node.serviceId ? `Service: ${node.serviceId}` : "",
        node.replicaGroupId ? `Replica group: ${node.replicaGroupId}` : "",
        node.runtimeRevisionId ? `Revision: ${node.runtimeRevisionId}` : "",
        node.internetReachability ? formatInternetReachabilityTitle(node) : ""
    ]
        .filter(Boolean)
        .join("\n");
}

function formatInternetReachabilityTitle(node) {
    return node.internetReachability === "inferred"
        ? "Possible internet connectivity inferred"
        : "Internet connectivity projected";
}

function getNodeRadius(node) {
    if (node.nodeKind === "service") {
        return 98;
    }

    return node.nodeKind === "replica" ? 78 : 86;
}

function getNodeId(value) {
    return typeof value === "string" ? value : value.id;
}

function getClassName(value) {
    return String(value || "generic").toLowerCase().replace(/[^a-z0-9_-]+/g, "-");
}

function getInitials(value) {
    return String(value || "?")
        .split(/[\s:_-]+/)
        .filter(Boolean)
        .slice(0, 2)
        .map(part => part[0])
        .join("")
        .toUpperCase() || "?";
}

function trimText(value, maxLength) {
    const text = String(value || "");
    return text.length > maxLength ? `${text.slice(0, maxLength - 1)}...` : text;
}
