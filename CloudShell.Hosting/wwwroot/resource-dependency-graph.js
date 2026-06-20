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

    update(resources) {
        const changed = this.hasStructureChanged(resources);
        const previousNodes = new Map(this.nodes.map(node => [node.id, node]));
        const degreeMap = this.getDegrees(resources);

        this.resources = resources;
        this.nodes = resources.map(resource => {
            const existing = previousNodes.get(resource.id);
            return {
                ...existing,
                id: resource.id,
                label: resource.label,
                name: resource.name,
                type: resource.type,
                resourceClass: resource.resourceClass,
                endpointText: resource.endpointText,
                stateLabel: resource.stateLabel,
                stateClass: resource.stateClass,
                detailUrl: resource.detailUrl,
                degree: degreeMap.get(resource.id) || 1
            };
        });

        const visibleIds = new Set(resources.map(resource => resource.id));
        this.links = resources.flatMap(resource =>
            resource.dependsOn
                .filter(dependencyId => visibleIds.has(dependencyId))
                .map(dependencyId => ({
                    id: `${resource.id}->${dependencyId}`,
                    source: resource.id,
                    target: dependencyId
                })));

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

    hasStructureChanged(resources) {
        if (resources.length !== this.resources.length) {
            return true;
        }

        const oldIds = new Set(this.resources.map(resource => resource.id));
        if (resources.some(resource => !oldIds.has(resource.id))) {
            return true;
        }

        const edgeKeys = new Set(this.resources.flatMap(resource =>
            resource.dependsOn.map(dependencyId => `${resource.id}->${dependencyId}`)));
        return resources.some(resource =>
            resource.dependsOn.some(dependencyId => !edgeKeys.has(`${resource.id}->${dependencyId}`)));
    }

    getDegrees(resources) {
        const degrees = new Map(resources.map(resource => [resource.id, resource.dependsOn.length]));
        resources.forEach(resource => {
            resource.dependsOn.forEach(dependencyId => {
                degrees.set(dependencyId, (degrees.get(dependencyId) || 0) + 1);
            });
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
            .attr("class", "resource-graph-link")
            .attr("opacity", 0);

        newLinks.transition()
            .duration(140)
            .attr("opacity", 1);

        this.linkElements = newLinks.merge(this.linkElements);
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
            .attr("y", -48)
            .attr("width", 168)
            .attr("height", 96)
            .attr("rx", 6);

        newNodes.append("circle")
            .attr("class", node => `resource-graph-node-icon ${getClassName(node.resourceClass)}`)
            .attr("cx", -54)
            .attr("cy", -14)
            .attr("r", 19);

        newNodes.append("text")
            .attr("class", "resource-graph-node-initials")
            .attr("x", -54)
            .attr("y", -9);

        newNodes.append("circle")
            .attr("class", node => `resource-graph-status ${node.stateClass}`)
            .attr("cx", 67)
            .attr("cy", -34)
            .attr("r", 9)
            .append("title");

        newNodes.append("text")
            .attr("class", "resource-graph-node-label")
            .attr("x", 0)
            .attr("y", 18);

        newNodes.append("text")
            .attr("class", "resource-graph-node-endpoint")
            .attr("x", 0)
            .attr("y", 36);

        newNodes.append("title")
            .attr("class", "resource-graph-node-title");

        newNodes.transition()
            .duration(140)
            .attr("opacity", 1);

        this.nodeElements = newNodes.merge(this.nodeElements);
        this.nodeElements.select(".resource-graph-node-icon")
            .attr("class", node => `resource-graph-node-icon ${getClassName(node.resourceClass)}`);
        this.nodeElements.select(".resource-graph-node-initials")
            .text(node => getInitials(node.label));
        this.nodeElements.select(".resource-graph-status")
            .attr("class", node => `resource-graph-status ${node.stateClass}`)
            .select("title")
            .text(node => node.stateLabel);
        this.nodeElements.select(".resource-graph-node-label")
            .text(node => trimText(node.label, 24));
        this.nodeElements.select(".resource-graph-node-endpoint")
            .text(node => trimText(node.endpointText, 28));
        this.nodeElements.select(".resource-graph-node-title")
            .text(node => `${node.label}\n${node.type}\n${node.endpointText}\n${node.stateLabel}`);

        function getClassName(value) {
            return String(value || "generic").toLowerCase();
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
    }

    selectNode(node) {
        this.selectedNode = this.selectedNode?.id === node.id ? null : node;
        this.updateHighlights();
    }

    openResource(node) {
        this.resourcesInterop.invokeMethodAsync("OpenResource", node.id);
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
