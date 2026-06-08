# Virtual Network Resource Proposal

## Status

Proposed.

## Problem

CloudShell already models endpoints, endpoint requests, endpoint mappings, and
networking capabilities. That is enough for local development and simple
mapping validation, but richer environments need a way to describe logical
network boundaries, ingresses, and provider-backed networking behavior without
coupling declarations to one runtime such as Docker Compose, Kubernetes, or an
Azure virtual network.

The resource model should support authored virtual network resources while
preserving the existing split:

- CloudShell owns projected resources, registration, dependencies, actions,
  capabilities, and endpoint mapping validation.
- Providers own runtime-specific configuration and state.
- Orchestrators materialize a resource graph for a runtime, but do not own the
  networking abstraction.

## Goals

- Model virtual networks as resources, not as a new top-level Control Plane
  primitive.
- Let authored resources declare ingress-like intent using endpoint requests
  and endpoint mappings.
- Let networking provider resources materialize mappings through capabilities
  such as gateway, load balancer, DNS, TLS, policy, or service discovery.
- Keep the default orchestrator useful for local development through logical
  localhost networking.
- Leave room for runtime-specific orchestrators to translate the same graph to
  Docker Compose, Kubernetes, Azure, or other environments.

## Non-Goals

- Do not introduce real network isolation in the default orchestrator.
- Do not standardize every ingress, TLS, DNS, or network policy field in the
  first version.
- Do not make orchestrators the owner of provider configuration.
- Do not expose provider-owned network controller state through resource
  attributes unless it is a stable, non-secret projected fact.

## Resource Model

A virtual network is a `Resource` with `ResourceClass.Network` and a precise
network type such as `cloudshell.virtualNetwork`.

It projects:

- endpoint requests owned by the network boundary
- endpoint mappings from network-owned endpoints to target resource endpoints
- dependencies on mapped targets and selected mapping providers
- networking capabilities that describe the roles it can play
- resource actions for reconciliation or provider-owned operations

The existing `cloudshell.network` type remains the basic logical network. A
virtual network extends the same model with richer capability and provider
selection. It should not require a separate projected entity shape.

Suggested capability identifiers:

- `networking.provider`: resource participates in network management.
- `networking.endpointProvider`: resource can assign or reserve endpoints.
- `networking.endpointMapper`: resource can materialize endpoint mappings.
- `networking.virtualNetwork`: resource represents a virtual network boundary.
- `networking.ingress`: resource can expose ingress-style endpoints.
- `networking.gateway`: resource can route traffic through a gateway.
- `networking.loadBalancer`: resource can distribute traffic across targets.
- `networking.serviceDiscovery`: resource can publish discovery records.
- `networking.policy`: resource can apply network policy.
- `networking.tls`: resource can manage TLS material or termination.

## Ingress Model

Ingress should initially compile down to endpoint requests and endpoint
mappings. That avoids a parallel concept while leaving room for richer authored
syntax.

Minimal declaration shape:

```csharp
var vnet = resources.AddVirtualNetwork(
    "network:app",
    "Application Network");

var api = resources.Declare("applications", "application:api");
var gateway = resources.Declare("networking", "networking:gateway");

var ingress = vnet.RequestHttpEndpoint(
    "api-public",
    host: "api.localhost",
    exposure: ResourceExposureScope.Public);

vnet.MapEndpoint(
    ingress,
    new ResourceEndpointReference(api.ResourceId, "http"),
    gateway,
    "ingress:api");
```

Future convenience syntax can layer on top:

```csharp
vnet.AddIngress("ingress:api")
    .WithHost("api.example.com")
    .WithPathPrefix("/v1")
    .WithTls("certificate:api")
    .MapTo(api, "http")
    .ProvidedBy(gateway);
```

That higher-level ingress builder should still produce endpoint requests,
endpoint mappings, dependencies, and provider references.

## Provider Responsibilities

A networking provider resource can be a gateway, load balancer, DNS publisher,
TLS manager, policy controller, or custom network controller running as a
managed resource.

Provider resources advertise capabilities. The Control Plane uses those
capabilities for validation and action capability reporting. Provider-owned
configuration remains behind provider contracts or authored resource
definitions.

For endpoint mappings:

1. The source endpoint must exist on the network resource.
2. The target endpoint must exist on the target resource.
3. The selected provider resource must exist.
4. The selected provider resource must advertise `networking.endpointMapper`.
5. Additional provider-owned validation can happen inside provider actions.

## Orchestrator Relationship

The orchestrator materializes the resource graph for a runtime. It should not
own the virtual-network abstraction.

The Control Plane and resource declarations define intent:

- network boundaries
- endpoint requests
- endpoint mappings
- dependencies
- provider resource selection
- capabilities

An orchestrator can inspect that graph and translate it to runtime artifacts:

- Docker Compose networks, published ports, and gateway containers
- Kubernetes namespaces, Services, Ingresses, Gateway API resources, and
  NetworkPolicies
- Azure virtual networks, subnets, private endpoints, application gateways, or
  DNS records
- local development localhost ports and logical mappings

If an orchestrator cannot materialize a capability, it should either leave the
behavior to the selected provider resource or expose a diagnostic/capability
reason. It should not silently imply isolation or routing that does not exist.

## Default Orchestrator Implementation

The default orchestrator should implement virtual networks as logical
host-local networks.

Supported behavior:

- Project virtual network resources as running logical resources.
- Reserve manual localhost endpoints.
- Auto-assign localhost ports from the configured Control Plane auto-port
  range.
- Treat provider-default endpoint requests as auto-assigned localhost endpoints
  unless a selected provider overrides them.
- Validate endpoint mappings through `reconcileEndpointMappings`.
- Preserve dependencies so starting a resource can start required target or
  provider resources according to normal dependency-start rules.
- Expose clear capabilities and action availability for local development.

Unsupported behavior:

- No real virtual network isolation.
- No subnets, route tables, firewall rules, or private DNS zones.
- No TLS termination unless a provider resource implements it.
- No load balancing beyond whatever a provider resource implements.
- No path-based or host-based routing unless represented by provider-owned
  configuration.

The default orchestrator should describe this mode as local logical networking.
It can prove the declaration shape and endpoint assignment behavior without
claiming cloud or container-network semantics.

## API and UI Projection

The HTTP API should continue to project virtual networks as ordinary resources.
No special virtual-network endpoint is required for the first version.

Expected API surface:

- `ResourceClass.Network`
- `TypeId` such as `cloudshell.virtualNetwork`
- projected endpoints
- resource capabilities
- `resourceActions` including reconciliation or provider-owned actions
- normal resource action capability responses

The UI should render virtual networks through the Resource Manager like other
network resources:

- show assigned endpoints
- show mapped targets
- show selected mapping provider
- expose reconcile action when present
- use provider-owned details when a provider supplies a detail route or tabs

Authoring UI can reuse the shared endpoint assignment component. Ingress
authoring should start as endpoint request plus mapping provider selection, and
only introduce a richer ingress editor once routing fields become standardized.

## Implementation Plan

1. Add a `cloudshell.virtualNetwork` resource type and builder convenience that
   wraps the existing network definition shape.
2. Add `networking.virtualNetwork` and `networking.ingress` capability
   identifiers.
3. Keep endpoint request and endpoint mapping persistence unchanged for the
   first version.
4. Add declaration tests showing virtual network endpoints, explicit provider
   mapping, dependencies, and capabilities.
5. Add Control Plane tests for default-orchestrator validation and action
   capabilities.
6. Add client/API contract coverage for projected capabilities and reconcile
   action invocation.
7. Add UI support only where the current network registration and detail
   surfaces need labels or provider selection for virtual-network resources.
8. Add sample declarations for local default-orchestrator behavior and one
   provider-backed ingress scenario.

## Open Questions

- Should `cloudshell.virtualNetwork` replace `cloudshell.network` over time, or
  remain a richer sibling type?
- Should standardized ingress fields include host, path prefix, TLS reference,
  and protocol routing in the first implementation, or stay provider-owned?
- Should subnet/address-space concepts be standardized, or only projected by
  specific infrastructure providers?
- How should conflicts be reported when multiple providers can materialize the
  same mapping?
- Should network reconciliation produce diagnostics as a structured result
  rather than only a procedure result message?
