# Networking

CloudShell models networking through resources, endpoint descriptors, endpoint
requests, resource endpoint contracts, topology mappings, exposure paths, DNS/name
mappings, and resource capabilities. The goal is to start with a simple
Aspire-like local workflow, then let the same resource graph grow into
host-managed or provider-managed networking for on-premise environments.
CloudShell borrows familiar cloud terminology where it helps users understand
the system, but keeps the underlying primitives explicit so application
endpoints, exposure paths, and naming do not collapse into provider-specific
concepts. The intent is not to hide networking artifacts; it is to expose them
through consistent CloudShell concepts first, then let provider-specific
implementation details remain inspectable where they help users diagnose or
operate the environment.

## Terminology

CloudShell uses these terms deliberately:

| Term | Model type | Meaning |
| --- | --- | --- |
| Endpoint descriptor | `ResourceEndpointDescriptor` | Resource type/kind metadata that announces a resource endpoint a resource can expose by default: endpoint name, protocol, target port, exposure default, assignment default, and whether the provider supports remapping that endpoint to another concrete port. |
| Endpoint request | `ResourceEndpointRequest` | Assignment intent asking a network or provider to reserve or assign an address for a resource endpoint. Requests can be manual, auto-assigned, provider-default, or predefined. |
| Resource endpoint | `ResourceEndpoint` | A resource-instance endpoint contract: stable endpoint name, protocol, target port, and exposure intent. Concrete reachable addresses are projected through endpoint network mappings. |
| Endpoint network mapping | `ResourceEndpointNetworkMapping` | A topology-specific resolved address for a resource endpoint, such as the local host address, virtual-network address, provider-owned ingress address, or public route address. |
| Configured endpoint mapping | `ResourceEndpointMappingDefinition` | A source-to-target mapping owned by a network resource, such as a network-owned frontend endpoint mapped to an application endpoint through a selected provider. |
| Exposure | Capability/resource relationship | A provider- or network-owned route from a boundary to a target endpoint, such as ingress, load-balancer route, gateway route, or host publishing. |
| DNS/name mapping | DNS/name resource | A human-facing or integration-facing name for a reachable endpoint or exposure route. |

## Core Model

Resources first announce the services they can expose through endpoint
descriptors. A descriptor is the resource-owned endpoint contract: stable
endpoint name, protocol, target port, default exposure, default
assignment behavior, and whether the provider supports port remapping. It can
exist before any concrete host address has been assigned.

Endpoint requests turn that endpoint contract into assignment intent. They ask
a network, runtime, or provider to reserve or assign an address for one resource
endpoint. Manual requests provide concrete address details. Auto or
provider-default requests let the selected network/provider choose from its
configured policy. Predefined requests describe an address chosen by provider
configuration outside the user's create flow.

Some endpoint contracts are expected by the resource type. In those cases the
user may omit endpoint requests because they do not care which address is
assigned. The resource still has a network binding, and Resource Manager or
the network controller can assign an endpoint from the descriptor and
environment policy. This should be resource-type-specific behavior, not an
assumption every resource with networking capabilities receives an endpoint
automatically.

The assigned endpoint is managed state. Endpoint requests are authoring input
for assignment, but Resource Manager should not require consumers to keep
reading the request to discover the address that was chosen. The network
binding is where the concrete port or address is allocated, even when the
caller explicitly requested one. Resource Manager and network controllers
should track and project the current assignment as a resource endpoint plus
endpoint network mapping, whether it came from a requested endpoint,
resource-type convention, automatic assignment, or environment policy.

Local development assignment should support the common "I do not care which
port this uses" case. The endpoint assignment is the address and port together.
On the Host network that normally means `localhost` plus a port, so multiple
resources of the same kind compete for the same conventional port. In a virtual
network, each resource can have its own virtual address, so the same
conventional port can be reused across different virtual hosts. Treat each
allocated virtual address as a resource-owned sub-host within the virtual
network. Treat that virtual address as a binding scope with host-like
semantics: it is not a separate subnet, but it owns a port allocation table in
the same way the Host network owns the `localhost` port table. A virtual host
may expose multiple resources or resource endpoints by using different ports.
The allocation key is `network + address + port`: the same virtual address and
port can be reused in another virtual network, but the same host-local address
and port cannot be bound twice on the developer machine.

Resource endpoints are created from endpoint descriptors plus endpoint
requests by the selected resource provider. They preserve the resource-owned
endpoint contract on the resource instance. Endpoint network mappings then
carry the topology-specific address, such as `localhost:<port>` in local
development, a private virtual-network address, a provider-owned ingress
endpoint, or an internal DNS-backed address in a managed topology.

## Automatic Endpoint Allocation Strategy

Endpoint allocation belongs to the network binding, not to the resource
declaration. A resource can bind to an explicit network or to the implicit
Host network used by local development. Once the binding exists, the network
controller owns choosing, validating, recording, and projecting the concrete
endpoint assignment for that topology.

A network binding can allocate a resource address separately from individual
endpoint ports. In a virtual network, for example, the controller may allocate
a virtual host address such as a private IP for the resource, then bind
multiple endpoint ports on that address. The uniqueness rule is therefore
topology-specific: host-local development usually cares about concrete
host/port assignments, while a virtual network may care about address
allocation plus per-address port bindings.

For virtual networks, treat the allocated address as the resource's virtual
host inside that network. It is not necessarily a separate CloudShell resource,
but it is a network-owned allocation below the virtual network boundary. The
virtual host address can carry multiple endpoint port bindings, and the
reachable endpoint is the tuple of network, virtual host address, protocol,
and port. This means Resource Manager can show a resource as having a specific
private address in a virtual network, then list the ports/endpoints bound on
that address.

The general allocation concern is dynamically assigning ports to addresses.
For the default Host network, the address is usually `localhost`, so the
binding is effectively `localhost + port`. For a virtual network, the address
can be a resource-specific virtual host address, so the binding is
`virtual host address + port`. The same endpoint model should cover both cases:
the network owns the address space and port bindings, while the resource owns
the endpoint contract that needs a binding.

Allocation starts from the resource endpoint descriptor. The descriptor tells
the network which endpoint name, protocol, target port, exposure default, and
assignment default the resource type expects. The declaration may override that
with a requested endpoint, but the request is still input to the network
controller rather than the source of truth for the assigned address.

The local-development strategy is:

1. Resolve the network binding. Use the explicit network when one is supplied;
   otherwise use the default network for the environment. Local development may
   provide an implicit Host network when policy allows host-local bindings.
2. Resolve the endpoint contract from the request or resource descriptor.
   Resource types without expected endpoint descriptors do not get automatic
   endpoints.
3. If the declaration requested a concrete host/IP and port, validate and use
   that assignment when policy and availability allow it.
4. If no concrete endpoint was requested, try the resource type's conventional
   local port when the descriptor declares one and policy allows it.
5. If the Host network endpoint is `Auto` and the resource descriptor supports
   port remapping, allocate a generated or mapped port from the network's
   configured range when the conventional port is unavailable or multiple
   instances need distinct host-local bindings. If the endpoint is
   `ProviderDefault`, fail when the conventional port is unavailable and ask for
   an explicit endpoint or an `Auto` endpoint on resource types that support
   remapping.
6. Record the result as the network-owned address and port binding, then
   project each reachable endpoint as a `ResourceEndpointNetworkMapping`.

Network resolution follows intent precedence. An endpoint request can target a
specific network; otherwise a resource-level network binding applies when the
resource type has one; otherwise the host or environment default network is
used. If no desired/default network is configured in a local-development host,
the Host network is the fallback binding scope. Dynamic allocation never
chooses a virtual network by itself: it allocates inside the network selected by
that resolution.

The same strategy applies to virtual networks, but the concrete assignment may
be a virtual address, private DNS name, provider-owned ingress address, or
another topology-specific endpoint instead of a localhost port. Resource
Manager should show the projected assignment and should not require callers to
inspect endpoint requests to discover the current address.

Samples may continue to request explicit ports when they need deterministic
copy/paste addresses or test assertions. The default development affordance
should be automatic assignment: if the developer does not care which endpoint
is used, the network should allocate and track one.

Containers are the typical case where this distinction matters: the container
image exposes an inner container port, while the runtime maps an external host,
virtual-network, load-balancer, or ingress endpoint to that inner port. That
remapping support is provider-owned. A resource provider declares whether an
endpoint can be remapped for the resource type and topology; the shell can then
show or disable the concrete port mapping controls accordingly.

Port remapping does not bypass network topology. It only decides which concrete
port a provider binds or publishes for a resource endpoint. The resulting
endpoint network mapping still belongs to a topology, such as the implied local
network, a container-host network, a virtual network, or a public exposure
path. Endpoint network mappings still decide where that endpoint is reachable
and which provider materializes that reachability.

Exposure paths and DNS/name mappings are relationships over resource endpoints
and their topology-resolved addresses.
They can connect a network-owned frontend, load-balancer route, gateway,
DNS/name mapping, or other topology artifact to the target resource endpoint.

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

Endpoint network mappings connect a resource endpoint to a network or topology
and provide the resolved address for that topology. For local
development, an Aspire-like helper such as `WithHttpEndpoint(port: 6000)`
declares an HTTP endpoint descriptor and creates assignment intent in the
default Host network whose resolved endpoint maps to the supplied
local port. That resolved mapping address is what the resource provider passes
to the service when it starts.

In managed or on-premise topologies, the concrete port chosen by a user is
often less important than the resource's network placement. A resource may be
assigned a private address inside a virtual network, or a private DNS name such
as `billing-api.internal.acme.net` derived from the resource name and the
environment's naming policy. The endpoint still carries the protocol and
target port, but the topology, DNS provider, and exposure policy decide which
address users and other services should call.

For application-style resources, the endpoint descriptor is the resource's
network-facing port contract. A virtual network can assign a virtual address to
that target port, and a DNS provider can publish a private name based on the
resource name and environment suffix. Those are endpoint network mappings and
name mappings over the resource endpoint; they do not change the resource's
declared target port.

Configured endpoint mappings connect one source endpoint to one target
endpoint. They are source-to-target mapping definitions owned by network
resources. A configured mapping can be validated by the network resource itself
or materialized by a selected networking provider resource.

Configured endpoint mappings are projected on network resources as first-class
resource data. They are not encoded as dependency metadata or comma-separated
attributes. This lets the Control Plane API, remote clients, and Resource
Manager UI show the mapping itself: source endpoint, target endpoint, and the
provider selected to materialize it.

During reconciliation, the network validates that each mapping source endpoint
belongs to the reconciled network resource and that a source endpoint is mapped
only once. The selected provider then validates and materializes any
runtime-specific behavior it owns. Provider-returned error signals are
surfaced as graph reconcile diagnostics or Resource Manager procedure signals
and must not be counted as successfully provisioned mappings.

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

The Resource Manager UI should use the same distinction:

- **Endpoint descriptor**: the resource-owned named protocol and target port.
  It describes what the resource can expose, such as a named HTTP port, TCP
  port, container target port, or provider-assigned logical endpoint.
- **Resolved endpoint**: the current address for an endpoint descriptor in a
  specific topology, resolved by network, runtime, or provider behavior.
- **Exposure**: the provider- or network-owned route to an endpoint. This can
  be a virtual-network endpoint mapping, load-balancer route, ingress rule, or
  another provider-owned reachability path.
- **DNS/name mapping**: the human-facing or integration-facing name for a
  reachable endpoint or exposure route.
- **Overview address**: the best current display address for the user. It may
  be a public URL, private or virtual-network URL, DNS name, local development
  endpoint, or resource-specific convenience value such as a SQL connection
  string. It is not the complete endpoint model.

| Layer | Shape | Primary use |
| --- | --- | --- |
| Resource endpoint descriptor | `http:80`, `tds:1433`, `metrics:9090` | Resource type/kind or instance contract that announces what target port the resource can expose. |
| Concrete endpoint address | `http://127.0.0.1:5218`, `tcp://10.0.0.5:1433`, `http://container-name:8080` | A provider-observed address that can be used directly when the caller is already in the right network context. |
| Topology-scoped reachability | host network, virtual network, container-host network, load-balancer backend | Defines where an endpoint is reachable and what provider may route, proxy, isolate, or materialize it. |
| Developer service discovery alias | `services__catalog-api__http__0`, `https+http://catalog-api` | Aspire-compatible per-workload configuration for local/programmatic development flows. |
| Network-level service discovery name | future DNS or registry-backed name inside a host or virtual network | Shared managed discovery for workloads in the same network scope without tracking programmatic references or projecting per-application environment variables. |
| DNS/name mapping | `api.cloudshell.local`, `api.example.com` | Human-facing or integration-facing names mapped to a resource endpoint, load-balancer route, or other exposure target. |

The immediate MVP uses concrete endpoint addresses, topology-scoped endpoint
network mappings, Aspire-compatible developer service discovery aliases, and
logical DNS/name mappings. Network-level service discovery is a later provider
capability for host or virtual networks. It should not replace explicit
configured endpoint mappings or public DNS/name mappings.

## Endpoint Topology And Network Policy

Configured endpoint mappings and topology-resolved addresses must stay
separate. A resource can declare that it exposes an `http` endpoint on target
port `8080` without deciding that the endpoint must be reachable through
`localhost`, a public IP, or a tenant virtual network.

The current address is topology-specific:

- in local development, the default topology is the Host network, so
  the resolved address is often `localhost:<port>` or `127.0.0.1:<port>`
- in a container host, the binding might be a container-network address or a
  published host port
- in a virtual network, the binding might be a private address, internal DNS
  name, gateway endpoint, or provider-owned route
- in a public exposure scenario, the binding that users call may be a load
  balancer, ingress, or DNS-backed route rather than the resource process or
  container itself

This means an on-premise or managed environment can assign
`billing-api.internal.acme.net` and a virtual-network address to an
application's `http` target port while keeping public DNS and public endpoint
mapping as separate, explicit exposure decisions.

For local development, Resource Manager may let the user choose a fixed local
port because that is convenient for tools on the developer machine. For
managed or on-premise environments, Resource Manager should instead guide the
user toward network placement, internal DNS naming, and public exposure policy.
The default address might be a private IP or internal DNS name, while public
DNS and public endpoints remain explicit exposure choices.

Resource configuration should therefore follow this order:

1. The resource type or instance declares endpoint descriptors, such as
   endpoint name, protocol, target port, and whether provider-supported port
   remapping is available.
2. The resource is bound to a network or topology, such as the Host network
   for local development or a tenant virtual network for managed/on-premise
   use.
3. The resource declaration either supplies a requested endpoint or, when the
   resource type expects an endpoint, lets the network assign one
   automatically.
4. The network binding allocates and tracks the concrete endpoint assignment:
   conventional port when available, requested endpoint when supplied and
   allowed, or generated/mapped endpoint when needed.
5. The environment or network policy decides which binding modes are allowed:
   host-local, virtual-network-only, public exposure, DNS/name mapping, or
   provider-managed ingress.
6. Exposure is configured separately when callers outside that topology need to
   reach the endpoint.

Resource Manager has initial support for choosing endpoint assignment mode,
network, and manual host/port values in supported application registration
flows. This is intentionally the first UI layer over the domain model, not the
complete managed networking experience. Over time, Resource Manager should let
users choose the network or topology for each resource endpoint whenever the
environment allows it. In local development the Host network can
remain the default. In managed environments, the available choices should come
from environment policy, tenant membership, resource permissions, and provider
capabilities. A disabled or unavailable network choice should explain whether
the reason is policy, permission, missing provider capability, or missing
setup.

When a resource with network capabilities is created, Resource Manager should
offer the available network choices, defaulting to **Host network** when that
network exists and is allowed. For the selected network, the user can usually
choose between auto-assignment and a manual address or port when policy allows
manual assignment. Auto-assignment lets the selected network provider pick the
best mapping option for that topology, such as a stable local port, a
virtual-network address, a private DNS name, provider-owned ingress, or a
policy-guided combination of those mappings.

For local development, CloudShell can default endpoint-bearing resources to an
implicit Host network and allow localhost-resolved addresses because that keeps
the developer loop simple. For managed or on-premise environments, CloudShell
should be able to enforce stricter environment policy, such as:

```text
Require virtual network: true
Allow localhost binding: false, unless explicitly allowed
Allow public exposure: permission-gated
Allow DNS or host-file changes: permission-gated
```

This makes tenant separation an environment policy rather than a special case
inside each resource provider. Application resources, databases, secrets
stores, configuration stores, and other endpoint-capable resources can all use
the same endpoint request, endpoint network mapping, and configured endpoint
mapping model, while networks, gateways, load balancers, ingress providers,
and DNS resources decide how those endpoints are bound, exposed, and named.

Port-number collisions are topology-specific. Two resources can expose the same
target port when they are assigned different virtual-network addresses or
private DNS names. The conflict to prevent in local development is reusing the
same concrete host-local address and port, such as two resources both trying to
bind `localhost:5218`.

CloudShell can still provide Aspire-compatible and local-development helpers
that make this feel simple. For example, a helper may declare an application
HTTP endpoint and produce an endpoint mapping to the implied default local
network. In the current local development topology that mapping resolves to an
address such as `localhost:<port>` or `127.0.0.1:<port>`. The helper should
compile down to the same primitives rather than becoming the canonical model.
The canonical model is:

- the resource type or instance declares a named protocol and target-port
  endpoint descriptor
- a network or topology resolves that descriptor into a reachable endpoint
- exposure resources or providers route traffic across boundaries when needed
- DNS/name mappings assign human-facing names to reachable endpoints or routes

## Ingress and Exposure

Ingress is the provider- or runtime-owned exposure path that accepts traffic
for a resource endpoint from a network boundary. It is a general exposure
concept for endpoint-capable resources, not a replacement for the resource
endpoint model:

- the resource owns the named protocol and target-port endpoint
- the network, runtime, or provider owns the exposure path to that endpoint
- DNS/name mappings can name either the reachable endpoint or the route/front
  that exposes it

Application resources and other endpoint-capable resources are the primary
endpoint owners. An application, database resource, configuration store
resource, secrets vault resource, or other resource that provides a service
should remain the thing users configure and operate. Ingress, load-balancers,
gateways, host publishing, and virtual-network mappings describe how callers
reach one of that resource's endpoints from a specific topology.

Infrastructure resources usually provide exposure instead of owning the app
contract. For example, a virtual network can own an endpoint mapping, a load
balancer can own a route table, and a DNS zone can own name mappings. Resources
without endpoint capability, such as a pure DNS zone or policy-like resource,
do not need ingress or endpoint views.

For the MVP, CloudShell should avoid a standalone `cloudshell.ingress` resource
type. Ingress should appear as one of these shapes instead:

- **App-owned ingress**: a provider-managed implementation detail for a
  container app endpoint, especially when replicas are enabled and traffic must
  be distributed across instances.
- **Virtual-network exposure**: an endpoint request plus endpoint mapping owned
  by a network resource and materialized by a provider with gateway/ingress
  capability.
- **Load balancer route**: an explicit user-managed front door when the user
  wants gateway-level control, shared routes, host/path/TCP rules, public
  endpoints, TLS, or multi-resource routing.

The Resource Manager UI should therefore present ingress as part of the
resource's Networking or Scaling experience before introducing a separate
Ingress resource. A container app can say that its endpoint is exposed through
provider-managed ingress, while a Load Balancer resource remains the explicit
resource for advanced routing.

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

When no network has been authored, CloudShell still projects the default Host
network resource as `network:host`. It is an implicit resource in the realized
environment model, not a special case outside the resource model. Local
development resources may default to it when policy allows localhost bindings,
so they can expose localhost endpoints without forcing the user to author a
network first.

The built-in host-local networking provider, `networking:host-local`, is
separate from `network:host`. `network:host` is the default topology boundary;
`networking:host-local` is the provider resource that can materialize local
proxies, host publishing, and other host-local mapping behavior.

Logical and virtual networks use the same endpoint request and configured
endpoint mapping model. Without an activated provider, virtual networks are
still useful for declaring intent and validating the graph, but they do not
imply real network isolation or routing.

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
controlPlane.DefineResources(resources =>
{
    var hostNetworking = resources.AddLocalHostNetwork("host-local");
    var api = resources.Declare("applications.dotnet-app", "application:api");

    var network = resources
        .AddVirtualNetwork("app")
        .WithDisplayName("Application Network");

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
endpoints, configured endpoint mappings, dependencies, and the reconcile
action. Target resources show a separate read-only "Network exposure" section
when a configured network mapping points at one of their endpoints. This is
intentionally separate from dependencies: a resource can be exposed through a
network without the resource owning or configuring that network. The settings
page warns when a virtual network requires a provider that is not active on the
current host.

The local host networking provider also projects the number of currently
provisioned endpoint mappings through `network.provisionedMappings`, so the
provider resource can show whether local proxy mappings have been materialized
after reconciliation.

Localhost exposure is a local-development convenience, not a default security
posture for shared or on-premise environments. A managed environment should be
able to disable user-created mappings that bind to `localhost`, loopback
interfaces, low ports, or other host-machine addresses unless an administrator
or privileged provider explicitly allows them. In those environments, users
should normally expose resources through provider-managed virtual networks,
ingress, load balancers, DNS/name mappings, or network-level service discovery
instead of direct host-local sockets.

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

After reconciliation, Resource Manager projects local host-name publishing
details onto each affected name mapping. The generated diagnostics show the
hosts-file target, whether resolver-cache refresh succeeded, failed, or was
skipped, and `.local` suffix warnings when applicable.

Programmatic declarations can use:

```csharp
resources
    .AddDnsZone("cloudshell-local", zoneName: "cloudshell.local")
    .WithDisplayName("CloudShell Local DNS")
    .UseLocalHostNames()
    .MapHost("app.cloudshell.local", app, "http");
```

Network-level service discovery remains a separate provider capability. The
current Aspire-compatible service discovery path is documented in
[Service discovery](service-discovery.md); future providers can add
DNS-backed or registry-backed discovery without changing the resource endpoint
or name-mapping model.

The HostVirtualNetwork sample demonstrates the manual version of this model:
services declare virtual-network-private endpoint mappings with distinct IP
addresses, DNS name mappings point at those private endpoints, and a
provider-owned CoreDNS zone-file publisher writes resolver configuration. This
proves the resource model shape before CloudShell adds automatic virtual IP
allocation or default service-discovery name generation.

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
