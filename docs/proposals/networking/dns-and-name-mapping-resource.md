# DNS and Name Mapping Resource Proposal

> Related to [Virtual Network Resource](virtual-network-resource.md) proposal.

## Status

Proposed.

## Problem

CloudShell can model applications, endpoints, virtual networks, ingress mappings, and load balancers, but it also needs a common way to describe how names resolve to those endpoints and how services are discovered inside a network boundary.

In local development this may mean `localhost`, `/etc/hosts`, Docker network aliases, or development domains such as `app.local`. In self-hosted and cloud-connected environments, the same intent may map to DNS zones, record sets, private DNS, ingress host rules, service discovery, Eureka-like registries, or provider-owned naming systems.

Without a common name-mapping model, DNS-like behavior risks becoming hidden inside load balancers, virtual networks, or provider-specific configuration.

## Goals

* Model DNS zones, records, and name mappings as resources.
* Keep name resolution separate from routing behavior.
* Allow local development providers to materialize names through host files, local resolvers, or development DNS.
* Allow cloud or infrastructure providers to materialize names through DNS zones, private DNS, network-level service discovery, or provider-specific naming systems.
* Let ingress, load balancer, service discovery, and virtual network resources reference stable names without owning DNS directly.
* Support both public and private name mappings.
* Keep provider-specific DNS behavior behind provider capabilities and actions.

## Non-Goals

* Do not require CloudShell to become a full DNS server.
* Do not standardize every DNS record type in the first version.
* Do not make load balancers own DNS records.
* Do not make virtual networks own all name resolution behavior.
* Do not replace the current Aspire-compatible application service-discovery environment-variable mapping.
* Do not require public DNS integration for local development.
* Do not standardize provider-specific DNS propagation, validation, or registrar behavior in the first version.

## Resource Model

DNS and name mapping should be modeled through ordinary CloudShell resources.

Suggested resource types:

* `cloudshell.dnsZone`
* `cloudshell.dnsRecord`
* `cloudshell.nameMapping`
* `cloudshell.serviceDiscovery`
* `cloudshell.publicAddress`
* `cloudshell.privateAddress`

Suggested capability identifiers:

* `networking.dns`
* `networking.dnsZone`
* `networking.dnsRecord`
* `networking.nameResolver`
* `networking.namePublisher`
* `networking.serviceDiscovery`
* `networking.publicAddress`
* `networking.privateAddress`

A DNS zone represents a naming boundary, such as:

* `local`
* `dev.local`
* `example.com`
* `internal.example.com`

A DNS record or name mapping connects a name to a target:

* public address
* private address
* virtual network endpoint
* ingress endpoint
* load balancer frontend
* service discovery entry
* provider-owned endpoint

Network-level service discovery is different from the current application
resource service-discovery mapping. Application `WithReference(...)` and
`WithServiceDiscovery()` produce workload configuration, usually environment
variables in the .NET service-discovery shape. Network-level service discovery
is infrastructure behavior: a resolver, registry, DNS provider, Eureka-like
service, mesh, gateway, or platform controller can translate a stable service
name to one or more reachable endpoints for resources inside a network
boundary.

## Declaration Model

Minimal declaration shape:

```csharp
var network = resources.AddVirtualNetwork(
    "network:app",
    "Application Network");

var publicAddress = resources.AddPublicAddress(
    "address:web",
    "Web Public Address");

var dns = resources.AddDnsZone(
    "dns:local",
    "local");

dns.MapHost(
    "app.local",
    publicAddress);
```

For ingress-driven mapping:

```csharp
var ingress = network.AddHttpEndpoint(
    "localhost",
    5080,
    "web-public",
    ResourceExposureScope.Public);

dns.MapHost(
    "app.local",
    ingress);
```

For provider-backed DNS:

```csharp
var dns = resources
    .AddDnsZone("dns:dev", "dev.local")
    .UseProvider("hosts-file");

dns.MapHost("api.dev.local", ingress);
```

Future convenience syntax can layer DNS onto ingress authoring:

```csharp
network.AddIngress("ingress:api")
    .WithHost("api.example.com")
    .WithDnsZone(dns)
    .MapTo(api, "http")
    .ProvidedBy(gateway);
```

That higher-level API should still produce ordinary resources, endpoint mappings, dependencies, and provider references.

## Name Mapping Rules

DNS and name mapping should follow a simple rule:

DNS names locate an address or endpoint. They do not own traffic routing.

Routing belongs to:

* ingress resources
* load balancers
* gateways
* service meshes
* endpoint mapping providers

Name mapping belongs to:

* DNS zones
* DNS records
* service discovery providers
* local resolver providers
* host networking providers

This keeps the model understandable:

```text
DNS name
    -> address or ingress endpoint
        -> gateway or load balancer
            -> backend target

service name
    -> service discovery registry or resolver
        -> endpoint, ingress, load balancer, or backend target
```

The DNS and service-discovery paths may be implemented by the same provider,
but the model should not require that. A network can use DNS records for
public names and a separate service registry for private service lookup.

## Provider Responsibilities

A DNS provider can materialize name mappings through different mechanisms depending on the runtime environment.

Examples:

* macOS host provider: `/etc/hosts`, resolver files, or local proxy integration.
* Docker provider: network aliases or embedded DNS.
* Traefik provider: host rules plus optional local name publication.
* Kubernetes provider: Services, DNS names, ingress hosts, or Gateway API hostnames.
* Service registry provider: Eureka, Consul, or another registry that resolves
  service names to endpoint sets inside a network boundary.
* Azure provider: public DNS zones, private DNS zones, public IP labels, or managed service hostnames.

Provider resources advertise capabilities such as:

* `networking.namePublisher`
* `networking.nameResolver`
* `networking.dnsZone`
* `networking.serviceDiscovery`

For a name mapping:

1. The DNS zone or name-mapping resource must exist.
2. The target address or endpoint must exist.
3. The selected provider resource must exist.
4. The selected provider resource must advertise the required DNS or name-publishing capability.
5. Provider-owned validation handles runtime-specific constraints.

## Relationship to Load Balancers and Ingress

Load balancers and ingress resources may reference DNS names, but they should not own DNS records directly.

For example:

```csharp
var lb = resources
    .AddLoadBalancer("public")
    .WithPublicAddress(publicAddress)
    .ExposeHttp(80)
    .ExposeHttps(443);

lb.MapHost("app.local", webApp, port: 80);

dns.MapHost("app.local", publicAddress);
```

The load balancer owns the host-routing rule.

The DNS zone owns the name-to-address mapping.

This allows the same load balancer rule to work with different DNS providers or no DNS provider at all.

## Relationship to Virtual Networks

Virtual networks may use private DNS or service discovery, but DNS and service discovery should remain separate capabilities.

A virtual network can depend on a DNS provider or name resolver:

```csharp
var network = resources.AddVirtualNetwork(
    "network:app",
    "Application Network");

var dns = resources
    .AddDnsZone("dns:internal", "internal.local")
    .WithScope(network);

dns.MapHost("api.internal.local", api, "http");
```

The virtual network defines the communication boundary.

The DNS zone defines names within that boundary.

The provider determines how those names are resolved. A different provider may
own network-level service discovery for the same boundary if the environment
uses a service registry instead of DNS-style names.

## Network-Level Service Discovery

CloudShell should support service discovery at the network layer after the MVP
local workflow is stable.

This should be provider-backed and boundary-aware:

* a virtual network or environment boundary can select a service discovery
  provider
* a service discovery provider can publish stable service names for resources,
  endpoints, ingress endpoints, load-balancer frontends, or backend pools
* providers can map names through DNS, a registry such as Eureka, Consul, or a
  platform-native resolver
* target resources can show read-only inbound discovery names in Resource
  Manager
* application-level environment-variable service discovery remains an
  application provider concern and should not be the only discovery mechanism
  for on-premise environments

This allows CloudShell to support both local Aspire-like service discovery and
on-premise network-level discovery without treating one as a compatibility
layer over the other.

## Default Orchestrator Implementation

The default orchestrator should treat DNS as logical name mapping unless a provider is selected that can materialize it.

Supported behavior:

* Project DNS zones and name mappings as ordinary resources.
* Project service discovery providers and discovery names as ordinary
  resources or provider-owned projected data when a provider owns the registry.
* Validate that mapping targets exist.
* Preserve dependencies between names, targets, and providers.
* Allow local providers to materialize mappings for development domains.
* Surface warnings when a mapping cannot be materialized on the current host.
* Allow ingress and load balancer resources to reference hostnames even when DNS is not managed by CloudShell.

Unsupported behavior:

* No built-in authoritative DNS server.
* No registrar integration.
* No automatic public DNS propagation.
* No automatic certificate validation through DNS challenges.
* No private DNS isolation unless a provider implements it.
* No guarantee that arbitrary local domains resolve without an activated provider.

## API and UI Projection

The HTTP API should project DNS zones, records, and name mappings as ordinary resources.

Expected API surface:

* resource class for networking or naming resources
* type IDs such as `cloudshell.dnsZone` and `cloudshell.dnsRecord`
* projected record names and target references
* provider references
* capabilities
* diagnostics and action capability reasons
* reconcile or publish actions when provided by a provider

The UI should show:

* DNS zones
* record names
* record targets
* exposure scope
* selected provider
* materialization status
* conflicts or warnings
* provider-owned details when available

Target resources should show inbound name mappings where useful, for example:

```text
api
├── endpoint: http
└── names:
    └── api.local -> api:http
```

## Implementation Plan

1. Add DNS and name-mapping resource type identifiers.
2. Add DNS/name-publishing capability identifiers.
3. Add builder APIs for DNS zones and host mappings.
4. Represent mappings as ordinary resources or provider-owned configuration with projected target references.
5. Add validation for target existence and provider capability.
6. Add UI projection for DNS zones and name mappings.
7. Add default-orchestrator diagnostics for unmapped or unmaterialized names.
8. Add a local development provider for host-based name publication.
9. Add sample declarations for local DNS-style mappings.
10. Add provider-backed examples for load balancer and virtual network integration.
11. Add a post-MVP sample that uses network-level service discovery through a
    provider such as a local registry or Eureka-like service.

## Remaining Tasks

* Decide whether DNS records should always be first-class resources or whether simple mappings can be projected from provider configuration.
* Add conflict detection for duplicate names in the same scope.
* Add local host-provider implementation.
* Add provider diagnostics for names that cannot be published.
* Add UI affordances for name mappings on target resources.
* Add integration examples with virtual networks, load balancers, and ingress resources.
* Decide the first provider-backed network-level service discovery sample.

## Open Questions

* Should `DnsRecord` be a child resource under `DnsZone`, or should name mappings be separate root resources?
* Should CloudShell standardize only A, AAAA, and CNAME records first?
* Should service discovery names be modeled as DNS records, endpoint aliases, or a separate resource type?
* Should local development domains default to `.local`, `.localhost`, or a configurable suffix?
* Should DNS zones have public/private exposure scope?
* Should DNS mappings target addresses, endpoints, ingress resources, or all of them?
* Should DNS validation include conflict detection across providers?
* Should TLS certificate resources depend on DNS records for validation workflows?
* Should provider-owned ingress host rules automatically suggest DNS mappings?
* How should CloudShell represent names that are externally managed and should not be reconciled?
