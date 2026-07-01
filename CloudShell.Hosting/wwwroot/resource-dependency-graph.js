import './d3.v7.min.js';

let graph = null;

export function initializeResourceDependencyGraph(selector, resourcesInterop) {
    disposeResourceDependencyGraph();
    graph = new ResourceDependencyGraph(selector, resourcesInterop);
}

export function updateResourceDependencyGraph(resources) {
    if (graph) {
        graph.update(resources || []);
    }
}

export function disposeResourceDependencyGraph() {
    if (graph) {
        graph.dispose();
        graph = null;
    }
}

class ResourceDependencyGraph {
    constructor(selector, resourcesInterop) {
        this.resourcesInterop = resourcesInterop;
        this.resources = [];
        this.nodes = [];
        this.links = [];
        this.selectedNode = null;
        this.svg = d3.select(selector);
        this.svg.selectAll("*").remove();
        this.baseGroup = this.svg.append("g").attr("class", "resource-graph-stage");
        this.linkGroup = this.baseGroup.append("g").attr("class", "resource-graph-links");
        this.linkLabelGroup = this.baseGroup.append("g").attr("class", "resource-graph-link-labels");
        this.nodeGroup = this.baseGroup.append("g").attr("class", "resource-graph-nodes");

        const defs = this.svg.append("defs");
        defs.append("marker")
            .attr("id", "resource-graph-arrow")
            .attr("viewBox", "0 -5 10 10")
            .attr("refX", 96)
            .attr("refY", 0)
            .attr("markerWidth", 9)
            .attr("markerHeight", 9)
            .attr("orient", "auto")
            .append("path")
            .attr("d", "M0,-5L10,0L0,5")
            .attr("class", "resource-graph-arrow");

        this.zoom = d3.zoom()
            .scaleExtent([0.25, 4])
            .on("zoom", event => {
                this.baseGroup.attr("transform", event.transform);
            });
        this.svg.call(this.zoom);

        this.linkForce = d3.forceLink()
            .id(node => node.id)
            .strength(0.9)
            .distance(link => {
                const degree = Math.max(link.source.degree || 1, link.target.degree || 1);
                return Math.min(180 + degree * 12, 280);
            });

        this.simulation = d3.forceSimulation()
            .force("link", this.linkForce)
            .force("charge", d3.forceManyBody().strength(-920))
            .force("collide", d3.forceCollide(node => Math.min(92 + (node.degree || 1) * 12, 170)).iterations(8))
            .force("x", d3.forceX().strength(0.18))
            .force("y", d3.forceY().strength(0.28))
            .force("center", d3.forceCenter().strength(0.02))
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
        const container = this.svg.node()?.closest(".resource-graph-shell");
        if (container) {
            this.resizeObserver.observe(container);
        }
        this.resize();
    }

    registerControls() {
        d3.select(".resource-graph-zoom-in").on("click.resourceGraph", () => this.zoomBy(1.35));
        d3.select(".resource-graph-zoom-out").on("click.resourceGraph", () => this.zoomBy(1 / 1.35));
        d3.select(".resource-graph-reset").on("click.resourceGraph", () => this.resetZoom());
    }

    zoomBy(scale) {
        this.svg.transition().duration(160).call(this.zoom.scaleBy, scale);
    }

    resetZoom() {
        this.svg.transition().duration(180).call(this.zoom.transform, d3.zoomIdentity);
    }

    resize() {
        const element = this.svg.node();
        const container = element?.closest(".resource-graph-shell");
        if (!element || !container) {
            return;
        }

        const width = Math.max(container.clientWidth, 320);
        const height = Math.max(container.clientHeight, 360);
        this.svg.attr("viewBox", `${-width / 2} ${-height / 2} ${width} ${height}`);
    }

    update(graph) {
        const nodes = graph?.nodes || [];
        const links = graph?.links || [];
        const changed = this.hasStructureChanged(nodes, links);
        const previousNodes = new Map(this.nodes.map(node => [node.id, node]));
        const degreeMap = this.getDegrees(nodes, links);

        this.resources = nodes;
        this.nodes = nodes.map(node => {
            const existing = previousNodes.get(node.id);
            return {
                ...existing,
                id: node.id,
                label: node.label,
                name: node.name,
                type: node.type,
                iconName: node.iconName,
                resourceClass: node.resourceClass,
                nodeKind: node.nodeKind,
                endpointText: node.endpointText,
                stateLabel: node.stateLabel,
                stateClass: node.stateClass,
                detailUrl: node.detailUrl,
                resourceId: node.resourceId,
                internetReachability: node.internetReachability,
                degree: degreeMap.get(node.id) || 1
            };
        });

        const visibleIds = new Set(this.nodes.map(node => node.id));
        this.links = links
            .filter(link => visibleIds.has(link.source) && visibleIds.has(link.target))
            .map(link => ({
                id: `${link.source}->${link.label}->${link.target}`,
                source: link.source,
                target: link.target,
                label: link.label,
                kind: link.kind,
                resourceId: link.resourceId
            }));

        this.renderLinks();
        this.renderNodes();

        this.simulation.nodes(this.nodes);
        this.linkForce.links(this.links);

        if (changed) {
            this.simulation.stop();
            this.simulation.alpha(1);
            for (let i = 0; i < 220; i++) {
                this.simulation.tick();
            }
        }

        this.simulation.alpha(0.55).restart();
        this.updateHighlights();
    }

    hasStructureChanged(nodes, links) {
        if (nodes.length !== this.resources.length ||
            links.length !== this.links.length) {
            return true;
        }

        const oldIds = new Set(this.resources.map(resource => resource.id));
        if (nodes.some(node => !oldIds.has(node.id))) {
            return true;
        }

        const edgeKeys = new Set(this.links.map(link => `${getNodeId(link.source)}->${link.label}->${getNodeId(link.target)}`));
        return links.some(link => !edgeKeys.has(`${link.source}->${link.label}->${link.target}`));
    }

    getDegrees(nodes, links) {
        const degrees = new Map(nodes.map(node => [node.id, 0]));
        links.forEach(link => {
            degrees.set(link.source, (degrees.get(link.source) || 0) + 1);
            degrees.set(link.target, (degrees.get(link.target) || 0) + 1);
        });
        return degrees;
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
            .attr("class", link => `resource-graph-link ${getClassName(link.kind)}`)
            .attr("opacity", 0);

        newLinks.transition()
            .duration(140)
            .attr("opacity", 1);

        this.linkElements = newLinks.merge(this.linkElements)
            .attr("class", link => `resource-graph-link ${getClassName(link.kind)}`);

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
            .attr("class", "resource-graph-link-label")
            .attr("opacity", 0);

        newLabels.transition()
            .duration(140)
            .attr("opacity", 1);

        this.linkLabelElements = newLabels.merge(this.linkLabelElements)
            .attr("class", link => `resource-graph-link-label ${getClassName(link.kind)}`)
            .text(link => trimText(link.label, 16));
    }

    renderNodes() {
        this.nodeElements = this.nodeGroup
            .selectAll(".resource-graph-node")
            .data(this.nodes, node => node.id);

        this.nodeElements.exit()
            .transition()
            .duration(140)
            .attr("opacity", 0)
            .remove();

        const newNodes = this.nodeElements.enter()
            .append("g")
            .attr("class", "resource-graph-node")
            .attr("opacity", 0)
            .call(this.drag)
            .on("click", (_event, node) => this.selectNode(node))
            .on("dblclick", (_event, node) => this.openResource(node))
            .on("mouseover", (_event, node) => {
                this.hoveredNode = node;
                this.updateHighlights();
            })
            .on("mouseout", () => {
                this.hoveredNode = null;
                this.updateHighlights();
            });

        newNodes.append("rect")
            .attr("class", "resource-graph-node-card")
            .attr("x", -84)
            .attr("y", -52)
            .attr("width", 168)
            .attr("height", 104)
            .attr("rx", 6);

        newNodes.append("circle")
            .attr("class", node => `resource-graph-node-icon-frame ${getClassName(node.resourceClass)}`)
            .attr("cx", -62)
            .attr("cy", -26)
            .attr("r", 12);

        newNodes.append("path")
            .attr("class", "resource-graph-node-icon-glyph")
            .attr("transform", "translate(-69,-33) scale(.7)");

        newNodes.append("circle")
            .attr("class", node => `resource-graph-status ${node.stateClass}`)
            .attr("cx", 67)
            .attr("cy", -34)
            .attr("r", 9)
            .append("title");

        const internetBadge = newNodes.append("g")
            .attr("class", "resource-graph-internet-badge");

        internetBadge.append("circle")
            .attr("class", "resource-graph-internet-badge-frame")
            .attr("cx", 42)
            .attr("cy", -34)
            .attr("r", 9);

        internetBadge.append("text")
            .attr("class", "resource-graph-internet-badge-icon")
            .attr("x", 42)
            .attr("y", -30);

        internetBadge.append("title");

        newNodes.append("text")
            .attr("class", "resource-graph-node-label")
            .attr("x", 0)
            .attr("y", 13);

        newNodes.append("text")
            .attr("class", "resource-graph-node-type")
            .attr("x", 0)
            .attr("y", 31);

        newNodes.append("text")
            .attr("class", "resource-graph-node-endpoint")
            .attr("x", 0)
            .attr("y", 46);

        newNodes.append("title")
            .attr("class", "resource-graph-node-title");

        newNodes.transition()
            .duration(140)
            .attr("opacity", 1);

        this.nodeElements = newNodes.merge(this.nodeElements);
        this.nodeElements
            .attr("class", node => `resource-graph-node ${getClassName(node.nodeKind)}`);
        this.nodeElements.select(".resource-graph-node-icon-frame")
            .attr("class", node => `resource-graph-node-icon-frame ${getClassName(node.nodeKind)} ${getClassName(node.resourceClass)}`);
        this.nodeElements.select(".resource-graph-node-icon-glyph")
            .attr("d", node => getResourceIconPath(node.iconName || node.type || node.resourceClass));
        this.nodeElements.select(".resource-graph-status")
            .attr("class", node => `resource-graph-status ${node.stateClass}`)
            .select("title")
            .text(node => node.stateLabel);
        this.nodeElements.select(".resource-graph-internet-badge")
            .attr("display", node => node.internetReachability ? null : "none")
            .attr("class", node => `resource-graph-internet-badge ${getClassName(node.internetReachability)}`);
        this.nodeElements.select(".resource-graph-internet-badge-icon")
            .text("↗");
        this.nodeElements.select(".resource-graph-internet-badge title")
            .text(node => node.internetReachability === "inferred" ? "Possible internet connectivity inferred" : "Internet connectivity projected");
        this.nodeElements.select(".resource-graph-node-label")
            .text(node => trimText(node.label, 24));
        this.nodeElements.select(".resource-graph-node-type")
            .text(node => trimText(node.type, 28));
        this.nodeElements.select(".resource-graph-node-endpoint")
            .text(node => trimText(node.endpointText, 28));
        this.nodeElements.select(".resource-graph-node-title")
            .text(node => `${node.label}\n${node.type}\n${node.endpointText}\n${node.stateLabel}`);

        function trimText(value, maxLength) {
            const text = String(value || "");
            return text.length > maxLength ? `${text.slice(0, maxLength - 1)}...` : text;
        }
    }

    selectNode(node) {
        this.selectedNode = this.selectedNode?.id === node.id ? null : node;
        this.updateHighlights();
    }

    openResource(node) {
        if (node.resourceId) {
            this.resourcesInterop.invokeMethodAsync("OpenResource", node.resourceId);
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

    dispose() {
        this.simulation.stop();
        this.resizeObserver?.disconnect();
        d3.select(".resource-graph-zoom-in").on("click.resourceGraph", null);
        d3.select(".resource-graph-zoom-out").on("click.resourceGraph", null);
        d3.select(".resource-graph-reset").on("click.resourceGraph", null);
        this.svg.on(".zoom", null);
        this.svg.selectAll("*").remove();
    }
}

function getNodeId(value) {
    return typeof value === "string" ? value : value.id;
}

function getClassName(value) {
    return String(value || "generic").toLowerCase().replace(/[^a-z0-9_-]+/g, "-");
}

function getResourceIconPath(value) {
    const normalized = String(value || "").trim().toLowerCase();

    if (normalized.includes("secret") || normalized.includes("vault") || normalized.includes("key") ||
        normalized.includes("identity") || normalized.includes("permission") || normalized.includes("access-control")) {
        return "M7 11a3 3 0 1 1 2.8-4H16v3h-2v2h-2v2H9.8A3 3 0 0 1 7 11z";
    }

    if (normalized.includes("sql-database") || normalized.includes("database-item")) {
        return "M5 6c0-1.1 2.2-2 5-2s5 .9 5 2v8c0 1.1-2.2 2-5 2s-5-.9-5-2V6zM5 6c0 1.1 2.2 2 5 2s5-.9 5-2M5 10c0 1.1 2.2 2 5 2s5-.9 5-2";
    }

    if (normalized.includes("database") || normalized.includes("sql")) {
        return "M4 6c0-1.1 2.7-2 6-2s6 .9 6 2v8c0 1.1-2.7 2-6 2s-6-.9-6-2V6zM4 6c0 1.1 2.7 2 6 2s6-.9 6-2M4 10c0 1.1 2.7 2 6 2s6-.9 6-2";
    }

    if (normalized.includes("storage") || normalized.includes("volume")) {
        return "M4 6h12v10H4zM4 9h12M7 13h2";
    }

    if (normalized.includes("route") || normalized.includes("load-balancer") || normalized.includes("loadbalancer")) {
        return "M5 6h5a3 3 0 0 1 0 6H7M7 12l2-2M7 12l2 2M14 6l2-2M14 6l2 2";
    }

    if (normalized.includes("network") || normalized.includes("dns") || normalized.includes("mapping") ||
        normalized.includes("endpoint") || normalized.includes("ingress")) {
        return "M10 4l5 3v6l-5 3-5-3V7zM5 7l5 3 5-3M10 10v6";
    }

    if (normalized.includes("container") || normalized.includes("replica") || normalized.includes("runtime")) {
        return "M10 4l5 3v6l-5 3-5-3V7zM5 7l5 3 5-3M10 10v6";
    }

    if (normalized.includes("configuration") || normalized.includes("settings") || normalized.includes("provisioning")) {
        return "M10 5v2M10 13v2M5 10h2M13 10h2M7.2 7.2l1.4 1.4M11.4 11.4l1.4 1.4M12.8 7.2l-1.4 1.4M8.6 11.4l-1.4 1.4M8 10a2 2 0 1 0 4 0 2 2 0 0 0-4 0z";
    }

    if (normalized.includes("web") || normalized.includes("internet")) {
        return "M10 4a6 6 0 1 0 0 12 6 6 0 0 0 0-12zM4 10h12M10 4c1.5 1.6 2.2 3.6 2.2 6s-.7 4.4-2.2 6M10 4c-1.5 1.6-2.2 3.6-2.2 6s.7 4.4 2.2 6";
    }

    if (normalized.includes("service") || normalized.includes("docker")) {
        return "M5 5h10v4H5zM5 11h10v4H5zM8 7h.01M8 13h.01";
    }

    return "M5 5h10v10H5zM8 8h4M8 11h4";
}

function trimText(value, maxLength) {
    const text = String(value || "");
    return text.length > maxLength ? `${text.slice(0, maxLength - 1)}...` : text;
}
