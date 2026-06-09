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

Endpoint mappings connect one source endpoint to one target endpoint. A mapping
can be validated by the network resource itself or materialized by a selected
networking provider resource.

Endpoint mappings are projected on network resources as first-class resource
data. They are not encoded as dependency metadata or comma-separated
attributes. This lets the Control Plane API, remote clients, and Resource
Manager UI show the mapping itself: source endpoint, target endpoint, and the
provider selected to materialize it.

Network resources have three current kinds:

- Host: the implicit default network when no network has been created.
- Logical: a named CloudShell boundary for endpoint assignment and mapping
  validation.
- Virtual: an environment boundary intended for host or provider-backed
  ingress, gateway, load-balancer, cluster, and service-discovery behavior.

## Capabilities

Resources advertise the roles they can play through capabilities. Important
networking capabilities include:

- `endpoint.source`: resource exposes endpoints.
- `networking.provider`: resource participates in network management.
- `networking.endpointProvider`: resource can assign or reserve endpoints.
- `networking.endpointMapper`: resource can materialize endpoint mappings.
- `networking.hostNetwork`: resource represents host networking.
- `networking.virtualNetwork`: resource represents a virtual network boundary.
- `networking.ingress`: resource can expose ingress-style endpoints.
- `networking.gateway`: resource can route traffic through a gateway.
- `networking.loadBalancer`: resource can distribute traffic across targets.
- `networking.backendPool`: resource represents a load-balancer target set.

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

## macOS Host Networking

The first host-provided virtual networking implementation targets macOS.

The built-in macOS host networking resource is:

```text
networking:host-macos
```

It advertises `networking.provider`, `networking.endpointMapper`,
`networking.gateway`, `networking.ingress`, and `networking.hostNetwork`.

When a virtual network mapping selects `networking:host-macos`, the Control
Plane validates the source endpoint, target endpoint, and provider capability.
The macOS provisioner then materializes the mapping as a local TCP proxy from
the network-owned endpoint to the target endpoint. This supports HTTP, HTTPS,
and TCP endpoints without requiring privileged packet-filter or virtual-adapter
configuration for the first implementation.

The authoring shape is:

```csharp
cloudShell.Resources(resources =>
{
    var hostNetworking = resources.AddMacOSHostNetworking();
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

- [Virtual Network Resource Proposal](proposals/virtual-network-resource.md)
- [Load Balancer Resource Proposal](proposals/load-balancer-resource.md)
- [Programmatic resources](programmatic-resources.md)
- [Domain model](domain-model.md)
- [Roadmap](roadmap.md)
