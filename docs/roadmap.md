# Roadmap

This roadmap describes the next product direction for CloudShell. It links to
the detailed proposals and domain docs rather than duplicating every design
detail here.

CloudShell is moving toward an on-premise control plane: a resource shell where
teams can model applications first, then add environment infrastructure,
networking, deployment, and operational control as the solution grows.

## Current Foundation

The current foundation is a resource model with programmatic declarations,
Resource Manager UI, provider-owned configuration, and a Control Plane API.

Useful references:

- [Domain model](domain-model.md)
- [System design guidelines](system-design-guidelines.md)
- [Programmatic resources](programmatic-resources.md)
- [Control Plane API](control-plane-api.md)
- [CloudShell and Aspire](cloudshell-and-aspire.md)

## Near-Term Roadmap

### 1. Host-Provided Virtual Networking

Goal: make virtual networking work when the host can provide it.

CloudShell should model host, logical, and virtual networks as resources. The
default host network remains available when no network has been created. A
virtual network can start in logical-only mode, but when a host networking
service is activated, CloudShell should configure virtual-network endpoint
mappings through that provider.

The first provider targets macOS and materializes mappings as local TCP
proxies. This is the next priority because it turns CloudShell from a local app
graph into an environment control plane that can manage on-premise networking.

References:

- [Networking](networking.md)
- [Virtual Network Resource Proposal](proposals/virtual-network-resource.md)
- [Domain model: Endpoint and networking](domain-model.md#endpoint-and-networking)
- [Programmatic resources: networks and services](programmatic-resources.md)

### 2. Load Balancing

Goal: expose stable endpoints over logical backend targets.

After host-provided virtual networking works, CloudShell should add
load-balancing support. Public or network endpoints should map to a stable
service or backend pool, not directly to every runtime replica.

Load-balancing behavior should be provider-owned at first, with CloudShell
standardizing only the common resource capabilities and target relationships
needed for validation, display, and orchestration.

References:

- [Virtual Network Resource Proposal: Clustering and Load Balancing](proposals/virtual-network-resource.md#clustering-and-load-balancing)
- [Application resources](resources/application-resources.md)
- [Container apps](resources/container-apps.md)

### 3. Provider-Owned Replication

Goal: support replicas where the provider can implement them.

Replicas should be runtime instances behind a stable resource, service, or
backend pool. They may be projected as child resources for inspection, logs,
health, and operations, but consumers should not depend on replica IDs as the
stable deployment or routing contract.

This keeps the user-facing model stable while allowing container, process,
cluster, or host providers to implement replication differently.

References:

- [Domain model: Resource](domain-model.md#resource)
- [Container apps](resources/container-apps.md)
- [Virtual Network Resource Proposal](proposals/virtual-network-resource.md)

### 4. Deployment Concepts

Goal: decide whether CloudShell needs first-class deployment concepts.

`ResourceDefinition` and `Deployment` may become useful when CloudShell needs
to distinguish desired configuration from applied runtime state, versioned
rollouts, revision history, and deployment events. They should not be
introduced just to make virtual networking work.

For now, provider-owned configuration, projected resources, resource events,
and container app revisions cover the immediate need.

References:

- [Container apps](resources/container-apps.md)
- [Persistence](persistence.md)
- [Resource templates](resource-templates.md)
- [Progress](progress.md)

### 5. Container Application Environments

Goal: evaluate whether container apps need an explicit isolation boundary.

A container application environment may become the right model for container
app isolation, shared networking, host configuration, and runtime policy. That
decision should come after host-provided virtual networking and load balancing
are working, because those features will clarify the real isolation boundary.

References:

- [Container apps](resources/container-apps.md)
- [Virtual Network Resource Proposal](proposals/virtual-network-resource.md)
- [Hosting model](hosting-model.md)

## Tracking Work

The current task queue stays in [TODO](../TODO.md). Completed decisions and
verification expectations stay in [Progress](progress.md). Larger design
threads should live under [docs/proposals](proposals/).
