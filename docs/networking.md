# Networking

CloudShell models networking through resources, endpoints, endpoint requests,
endpoint mappings, and resource capabilities. The goal is to start with a
simple Aspire-like local workflow, then let the same resource graph grow into
host-managed or provider-managed networking for on-premise environments.

## Core Model

Endpoints are projected facts on resources. They describe addresses that exist
now, such as HTTP, HTTPS, TCP, UDP, or logical network addresses.

Endpoint requests are intent. They ask a network or provider to reserve or
assign an address. Requests can be manual, auto-assigned, provider-default, or
predefined by a provider.

Platform-owned endpoint assignments are validated before the platform saves a
network, service, or load-balancer resource. Concrete assignments are compared
by normalized host and port so one host-local socket cannot be assigned to two
platform resources, even when the endpoint protocol labels differ. Logical
endpoints and provider-projected runtime endpoints are not part of that first
validation pass.

Endpoint ownership and runtime port availability are related but separate.
Resource Manager should track CloudShell-owned endpoint intent and concrete
assignments so CloudShell resources do not knowingly reuse the same
host/protocol/port assignment. Host and runtime providers still own the final
bind or publish operation. For local host ports, the Control Plane runs an
advisory availability preflight before saving platform-owned network, service,
and load-balancer endpoint assignments, and application providers run similar
preflights before start. These checks are not authoritative because another
process can bind the port between the preflight and the actual start. Providers
must translate final bind failures into stable resource action diagnostics.

Dangling external processes are therefore diagnostics, not platform-owned
state. When a port is occupied by a process or container CloudShell does not
own, CloudShell should report that the endpoint assignment is unavailable and,
when the provider can observe it safely, include the owning process/container
identity. It should not claim the endpoint reservation unless the assignment is
part of CloudShell's resource graph or a persisted provider-owned runtime
artifact being recovered.

Endpoint mappings connect one source endpoint to one target endpoint. A mapping
can be validated by the network resource itself or materialized by a selected
networking provider resource.

Endpoint mappings are projected on network resources as first-class resource
data. They are not encoded as dependency metadata or comma-separated
attributes. This lets the Control Plane API, remote clients, and Resource
Manager UI show the mapping itself: source endpoint, target endpoint, and the
provider selected to materialize it.

During reconciliation, the network validates that each mapping source endpoint
belongs to the reconciled network resource and that a source endpoint is mapped
only once. The selected provider then validates and materializes any
runtime-specific behavior it owns.

The reconcile action uses the same validation path for action availability.
Resource Manager can disable the action and show a reason when a mapping
references a missing source or target endpoint, selects a provider without the
`networking.endpointMapper` capability, or requires a host-networking
provisioner that is not active on the current host.

Network resources have three current kinds:

- Host: the implicit default network when no network has been created.
- Logical: a named CloudShell boundary for endpoint assignment and mapping
  validation.
- Virtual: an environment boundary intended for host or provider-backed
  ingress, gateway, load-balancer, cluster, and service-discovery behavior.

## Addressing Layers

CloudShell should be explicit about the different ways a resource can be
addressed. These mechanisms are related, but they are not interchangeable.

| Layer | Shape | Primary use |
| --- | --- | --- |
| Concrete endpoint address | `http://127.0.0.1:5218`, `tcp://10.0.0.5:1433`, `http://container-name:8080` | A provider-observed address that can be used directly when the caller is already in the right network context. |
| Topology-scoped reachability | host network, virtual network, container-host network, load-balancer backend | Defines where an endpoint is reachable and what provider may route, proxy, isolate, or materialize it. |
| Developer service discovery alias | `services__catalog-api__http__0`, `https+http://catalog-api` | Aspire-compatible per-workload configuration for local/programmatic development flows. |
| Network-level service discovery name | future DNS or registry-backed name inside a host or virtual network | Shared managed discovery for workloads in the same network scope without tracking programmatic references or projecting per-application environment variables. |
| DNS/name mapping | `api.cloudshell.local`, `api.example.com` | Human-facing or integration-facing names mapped to a resource endpoint, load-balancer route, or other exposure target. |

The immediate MVP uses concrete endpoint addresses, topology-scoped endpoint
mappings, Aspire-compatible developer service discovery aliases, and logical
DNS/name mappings. Network-level service discovery is a later provider
capability for host or virtual networks. It should not replace explicit
endpoint mappings or public DNS/name mappings.

## Capabilities

Resources advertise the roles they can play through capabilities. The common
capability vocabulary is documented in [Resource capabilities](capabilities.md).
Networking uses capabilities such as `endpoint.source`,
`networking.endpointProvider`, `networking.endpointMapper`,
`networking.hostNetwork`, `networking.virtualNetwork`,
`networking.ingress`, `networking.gateway`, and
`networking.loadBalancer`.

Capabilities describe what a resource can provide. They do not move provider
configuration into the platform resource model. Provider-owned settings and
runtime state remain behind provider contracts.

## Default Behavior

When no network has been created, CloudShell projects the default host network
as `network:host`. This keeps local development simple: resources can still
expose localhost endpoints without forcing the user to create a network first.

Logical and virtual networks use the same endpoint request and endpoint mapping
model. Without an activated provider, virtual networks are still useful for
declaring intent and validating the graph, but they do not imply real network
isolation or routing.

## Local Host Networking

The first host-provided virtual networking implementation is portable local
host networking for macOS, Linux, and Windows.

The built-in local host networking resource is:

```text
networking:host-local
```

It advertises `networking.provider`, `networking.endpointMapper`,
`networking.gateway`, `networking.ingress`, and `networking.hostNetwork`.

When a virtual network mapping selects `networking:host-local`, the Control
Plane validates the source endpoint, target endpoint, and provider capability.
The local host networking provisioner then materializes the mapping as a local
TCP proxy from the network-owned endpoint to the target endpoint. This supports
HTTP, HTTPS, and TCP endpoints without requiring privileged packet-filter,
NAT, firewall, or virtual-adapter configuration for the first implementation.

The older `networking:host-macos` provider remains as a macOS-specific alias.
New samples and declarations should prefer `networking:host-local` unless they
are intentionally validating a macOS-specific provider path.

The authoring shape is:

```csharp
cloudShell.Resources(resources =>
{
    var hostNetworking = resources.AddLocalHostNetworking();
    var api = resources.Declare("applications", "application:api");

    var network = resources.AddVirtualNetwork(
        "network:app",
        "Application Network");

    var ingress = network.AddHttpEndpoint(
        "localhost",
        5290,
        "api-public",
        ResourceExposureScope.Public);

    network.MapEndpoint(
        ingress,
        new ResourceEndpointReference(api.ResourceId, "http"),
        hostNetworking,
        "mapping:api-public");
});
```

The Resource Manager UI shows network kind, host readiness, mapping providers,
endpoints, endpoint mappings, dependencies, and the reconcile action. Target
resources show a separate read-only "Network exposure" section when a network
mapping points at one of their endpoints. This is intentionally separate from
dependencies: a resource can be exposed through a network without the resource
owning or configuring that network. The settings page warns when a virtual
network requires a provider that is not active on the current host.

The local host networking provider also projects the number of currently
provisioned endpoint mappings through `network.provisionedMappings`, so the
provider resource can show whether local proxy mappings have been materialized
after reconciliation.

## DNS and Name Mapping

DNS and name mapping are modeled separately from virtual networks and load
balancers. A DNS zone resource defines the naming boundary, and name-mapping
resources point a host name at a target resource endpoint. This lets Resource
Manager show which names point at an application, load balancer, or other
endpoint without hiding name resolution inside routing configuration.

Name mappings can be logical-only or provider-backed. Logical-only mappings
record the intended name and target but do not publish DNS records. When a DNS
zone selects a publishing provider, the zone exposes a
`reconcileNameMappings` action so operators can apply or re-apply the expected
records.

The first local publishing provider is `local-hostnames`. It supports exact
host mappings and writes a CloudShell-managed block to a hosts-file style
target. The provider can be pointed at a custom file for safe development and
testing, and Resource Manager warns before creating `.local` names because
that suffix can conflict with mDNS/Bonjour on some hosts or networks.

When the provider writes to the system hosts file, it also attempts a
best-effort resolver cache refresh using fixed platform commands:
`dscacheutil -flushcache` and `killall -HUP mDNSResponder` on macOS,
`ipconfig /flushdns` on Windows, and `resolvectl flush-caches`,
`systemd-resolve --flush-caches`, or `nscd -i hosts` on Linux. Custom
hosts-file targets skip resolver refresh because they are normally used for
safe inspection and tests rather than host name resolution.

Programmatic declarations can use:

```csharp
resources
    .AddDnsZone("dns:cloudshell-local", "CloudShell Local DNS", "cloudshell.local")
    .UseLocalHostNames()
    .MapHost("app.cloudshell.local", app, "http");
```

Network-level service discovery remains a separate provider capability. The
current Aspire-compatible service discovery path is documented in
[Service discovery](service-discovery.md); future providers can add
DNS-backed or registry-backed discovery without changing the resource endpoint
or name-mapping model.

## Load Balancing and Clustering

Load balancing should build on virtual networking rather than replacing it. A
public or network endpoint should map to a stable target such as a service
resource, backend pool, container app, or application resource. Providers own
the runtime details: concrete replicas, node placement, target health,
balancing strategy, and traffic splitting.

This keeps the public contract stable while still allowing providers to project
runtime instances as child resources for inspection, logs, health, and
operations.

The first proposed load-balancer provider target is Traefik because it supports
dynamic configuration, HTTP host/path routing, TCP routing, TLS later, and both
containerized and host-managed operation. The proposed fluent API exposes
entrypoints and maps routes to target resource endpoints first, with raw target
ports available as a convenience when the target has not projected a named
endpoint yet.

## References

- [Virtual Network Resource Proposal](proposals/networking/virtual-network-resource.md)
- [Load Balancer Resource Proposal](proposals/networking/load-balancer-resource.md)
- [DNS and name mapping proposal](proposals/networking/dns-and-name-mapping-resource.md)
- [Service discovery](service-discovery.md)
- [Programmatic resources](programmatic-resources.md)
- [Domain model](domain-model.md)
- [Roadmap](roadmap.md)
