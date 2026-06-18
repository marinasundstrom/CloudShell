# Resource Capabilities

Resource capabilities describe the roles a projected resource can play in the
CloudShell environment. They are stable facts on `Resource`, exposed through
`Resource.ResourceCapabilities`, and identified by canonical IDs in
`ResourceCapabilityIds`.

Capabilities are not actions. A capability says that a resource can provide a
role such as endpoint source, environment-variable configuration, endpoint
mapping, or load balancing. Actions describe operations that can be invoked.
Action capabilities describe whether those actions can be invoked now.

## Design Rules

- Use CloudShell-defined IDs from `ResourceCapabilityIds` when a common role is
  already modeled.
- Use dotted lower-camel IDs for new common capabilities.
- Keep capability metadata stable, string-keyed, non-secret, and small.
- Prefer capabilities over hard-coded provider or resource-type checks when a
  workflow needs to discover which resource can provide a role.
- Keep provider-owned configuration behind provider contracts. A capability
  advertises that a resource can participate; it does not make the provider
  configuration platform-owned.

## Common Capabilities

### `endpoint.source`

The resource exposes endpoints that other resources, networks, load balancers,
or generated UI can reference. Workload resources commonly advertise this when
they project HTTP, HTTPS, TCP, UDP, or logical endpoints.

### `environment.variables`

The resource supports configured environment variables. Resource Manager can
use this capability with `IResourceEnvironmentVariableConfigurationProvider` to
assign literal values, configuration-entry references, or Secrets Vault
references to the resource.

This capability is not limited to application resources. Any provider can
advertise it when the resource has an environment-like execution surface and
the provider owns the persistence and update behavior.

### `monitoring`

The resource supports provider-observed resource monitoring. Resource Manager
can use this capability as the stable graph signal for a generated or
provider-owned Monitoring view, while
`IResourceMonitoringProvider.CanMonitor(...)` remains the runtime authority for
whether the current Control Plane can return a current snapshot for the
resource.

This capability is for resource metrics such as process or container CPU,
memory, process count, network I/O, block I/O, and provider-observed runtime
status. Application telemetry metrics remain separate and belong under
Telemetry.

### `networking.provider`

The resource participates in network management. This is the broad signal for
resources that can own or coordinate networking behavior, while more specific
networking capabilities describe the concrete role.

### `networking.endpointProvider`

The resource can assign or reserve endpoints. Network resources use this role
when they can satisfy endpoint requests such as manually assigned addresses,
auto-assigned local ports, or provider-defined endpoint defaults.

### `networking.endpointMapper`

The resource can materialize configured endpoint mappings. A selected provider
with this capability can connect a source endpoint to a target endpoint through
provider-owned runtime behavior such as a proxy, gateway, tunnel, or platform
route. The resulting reachable address can be projected separately as an
endpoint network mapping on the target resource.

### `networking.hostNetwork`

The resource represents host networking. The default host network and
host-provided networking integrations use this role for local machine boundary
behavior such as localhost endpoint exposure.

### `networking.virtualNetwork`

The resource represents a virtual network boundary. Virtual networks use the
same endpoint request and configured endpoint mapping model as logical networks
while leaving isolation, routing, and materialization to provider-owned
behavior.

### `networking.ingress`

The resource can expose ingress-style endpoints. This can be used by virtual
networks, gateways, reverse proxies, or load balancers that accept traffic at a
stable boundary and route it to target resources.

### `networking.gateway`

The resource can route traffic through a gateway. Gateways are provider-owned
runtime boundaries that can materialize configured endpoint mappings, ingress,
or other network traversal behavior.

### `networking.loadBalancer`

The resource can distribute traffic across targets. Load balancers keep the
stable CloudShell resource separate from provider-owned runtime configuration
and implementation containers.

### `networking.backendPool`

The resource represents a load-balancer target set. Backend pools group stable
targets for routing without making each runtime instance the stable user-facing
resource.

### `networking.cluster`

The resource represents a clustered runtime boundary. Cluster capabilities are
reserved for providers that can project or manage multiple nodes, schedulers,
or replicated runtime infrastructure.

### `networking.clusterNode`

The resource represents a node in a clustered runtime boundary. Nodes are
typically provider-projected resources used for inspection, diagnostics, or
placement hints rather than top-level application declarations.

### `networking.healthProbe`

The resource can evaluate target health for routing or traffic decisions. This
is useful for load balancers, gateways, and service meshes that need
provider-owned health evaluation.

### `networking.trafficSplit`

The resource can split traffic across versions, revisions, pools, or backends.
This is a routing capability, not a replica model by itself.

### `networking.serviceDiscovery`

The resource can publish or participate in service-discovery records. Workload
providers can use this with dependencies and references while keeping concrete
service-discovery implementation details provider-owned.

### `networking.dnsZone`

The resource represents a DNS or DNS-like naming boundary. The MVP logical
projection uses this to show zones that contain host/name mappings even when no
provider is publishing records yet.

### `networking.nameMapping`

The resource represents a logical name-to-target relationship, such as a host
name mapped to a resource endpoint. Providers may later materialize these
mappings through DNS, host files, service registries, ingress host rules, or
platform-native naming systems.

### `networking.namePublisher`

The resource can publish names through a provider-owned mechanism such as DNS,
local resolver configuration, host files, or a service registry.

### `networking.nameResolver`

The resource can resolve names for a network or environment boundary. This is
separate from application-level environment-variable service discovery.

### `networking.policy`

The resource can apply network policy. Policy configuration and enforcement
remain provider-owned unless CloudShell later standardizes a platform policy
model.

### `networking.egress`

The resource can manage or shape outbound traffic. This role covers future
egress gateways, NAT, firewall, or policy integrations without encoding those
provider-specific settings into the base resource projection.

### `networking.tls`

The resource can manage TLS material, TLS termination, or TLS routing behavior.
Secrets and certificates must not be projected through resource attributes or
capability metadata.

## Resource Action Capabilities

Resource capabilities are different from resource action capabilities.

Resource capabilities are projected on `Resource` and describe roles the
resource can provide. Resource action capabilities are returned through
`ResourceOperationCapabilities` and describe whether a specific resource action
can execute now, including authorization, state, provider support, dependency
warnings, and unavailable reasons.
