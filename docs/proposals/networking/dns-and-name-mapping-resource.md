# DNS and Name Mapping Resource Proposal

> Related to [Virtual Network Resource](virtual-network-resource.md) proposal.

## Status

In progress.

The first MVP slice projects logical `cloudshell.dnsZone` resources and child
`cloudshell.nameMapping` resources from programmatic declarations. This gives
Resource Manager an inspectable model for host names, target resources,
endpoint names, exposure scope, and provider intent without requiring
CloudShell to publish DNS records yet. DNS zones with provider intent now
advertise a provider-backed `reconcileNameMappings` action through the initial
`INamePublishingProvider` contract, but no built-in DNS publisher has been
selected as the default implementation.

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
They are logical model resources by default and do not need to expose
lifecycle status. A DNS zone or name mapping can report diagnostics,
materialization status, conflicts, and provider intent without projecting a
`ResourceState`; absence of state means the resource does not produce
lifecycle status, not that the status is unknown.

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
local workflow is stable. The MVP should still model names and show
relationships in Resource Manager so users can understand how public and
private endpoints are intended to be reached.

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
* Programmatically declare DNS zones and host mappings with `AddDnsZone(...)`
  and `MapHost(...)`.
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

DNS and name-mapping resources should not expose normal lifecycle state just
because a provider can publish records. DNS usually lives outside the
CloudShell host process, and published state can survive host restarts,
Control Plane crashes, or provider restarts. The correct operation is a
provider-backed reconcile/apply action, not Start or Stop.

The first standardized action should be:

```text
reconcileNameMappings
```

Display name: `Reconcile name mappings`.

This action can be advertised on DNS zone resources, name-publishing provider
resources, or both, depending on provider ownership. The action should:

* validate selected publisher resources and name-mapping conflicts
* ask the selected provider to publish or re-apply expected records
* remove or mark provider-owned stale records when the provider can safely own
  that scope
* return structured diagnostics for records that cannot be published
* refresh projected materialization state after the provider observes the
  external system

The provider contract is explicit through `INamePublishingProvider`. Logical
zones without a selected publisher stay honest by showing that no publisher is
selected rather than exposing a no-op apply button. Provider-backed zones can
surface `reconcileNameMappings`; action availability reports missing provider
implementations or invalid publisher resources. The publishing context carries
resolved name mappings, including the target resource, optional target
endpoint, and optional selected publisher resource, so providers do not need to
re-implement the Resource Manager lookup rules.

Projected DNS materialization should distinguish:

* logical-only: CloudShell models the name but no provider will publish it
* provider selected: a provider is responsible but applied state is unknown
* applied: the provider observed the expected external record
* drifted or failed: the provider observed missing, stale, or invalid external
  state

This lets Resource Manager answer the operational question: "Are the DNS
settings applied now?" without requiring the DNS zone or name mapping to be a
running resource.

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

Those mappings may represent internal DNS-style names or custom domain names.
The resource model records the relationship first; provider-backed publication
decides later whether and how a specific name is materialized.

## Implementation Plan

1. Add DNS and name-mapping resource type identifiers. Done for
   `cloudshell.dnsZone` and `cloudshell.nameMapping`.
2. Add DNS/name-publishing capability identifiers. Done for logical DNS zone,
   name mapping, name publisher, and name resolver capabilities.
3. Add builder APIs for DNS zones and host mappings. Done for
   `AddDnsZone(...)` and `MapHost(...)`.
4. Represent mappings as ordinary resources or provider-owned configuration
   with projected target references. Initial child-resource projection is in
   place for DNS zone mappings.
5. Add validation for target existence and provider capability. Initial
   logical conflict status is projected for duplicate host/scope mappings in
   the same DNS zone, and mappings now project whether a publishing provider
   is selected or the mapping is logical-only.
6. Add UI projection for DNS zones and name mappings. Initial target-side
   application overview projection is in place for inbound name mappings, and
   generated resource overviews surface logical name conflicts and logical-only
   materialization status as diagnostics. Generated diagnostics also warn when
   a selected name-publishing provider resource is missing or lacks the DNS
   publisher capability. DNS zones and name mappings are registered as
   inspectable Resource Manager resource types. Resource Manager can now
   create a DNS Zone, optionally include one initial name mapping, and add
   standalone name mappings to existing zones; name mappings can be deleted
   through the normal Resource Manager delete flow; update editing remains
   deferred.
7. Add default-orchestrator diagnostics for unmapped or unmaterialized names.
   Done for logical-only DNS name mappings without a selected publisher.
8. Add sample declarations for local DNS-style mappings. Done in the Load
   Balancer sample for `app.local` and `api.local` targeting the public
   load-balancer frontend.
9. Add provider-backed reconciliation infrastructure. Initial
   `INamePublishingProvider` and `reconcileNameMappings` action support is in
   place for DNS zones with provider intent. The action validates conflicts,
   selected publisher resources, and missing activated publisher
   implementations before delegating to the provider.
10. Add provider-backed examples for load balancer and virtual network integration.
11. Add a local development provider for host-based name publication.
12. Add a post-MVP sample that uses network-level service discovery through a
    provider such as a local registry or Eureka-like service.

## Remaining Tasks

* Add Resource Manager update authoring UI for existing name mappings owned by
  a DNS zone.
* Add provider-specific publish/materialization diagnostics from DNS provider
  runtime state.
* Decide whether DNS records should always be first-class resources or whether simple mappings can be projected from provider configuration.
* Add create/update blocking or guided resolution for duplicate names in the
  same scope when DNS/name mappings are authored through Resource Manager.
* Add local host-provider implementation for `INamePublishingProvider`.
* Add provider diagnostics for names that cannot be published.
* Add UI affordances for authoring and resolving name mappings.
* Add integration examples with virtual networks and ingress resources.
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
