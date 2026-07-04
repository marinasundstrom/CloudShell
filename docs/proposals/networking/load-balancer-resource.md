# Load Balancer Resource Proposal

## Status

In progress.

## Problem

CloudShell can now model networks, endpoints, endpoint requests, endpoint
mappings, and provider-backed host networking. The next step is a load balancer
resource that gives users a stable, provider-neutral way to expose HTTP, HTTPS,
and TCP traffic while allowing the concrete implementation to be Traefik,
Nginx, HAProxy, Envoy, a cloud load balancer, or a custom provider.

The abstraction should not assume that the provider runs in Docker. A load
balancer provider may be:

- a provider-owned runtime container on a selected host
- a process installed on the host
- a host service activated by the operator
- an appliance or external controller
- a future Kubernetes, Azure, or on-premise provider

## Provider Choice

Traefik should be the first built-in provider target.

Traefik is the best fit for the first provider because it supports dynamic
configuration, HTTP host and path routing, TCP routers, TLS termination, Docker
or file providers, and a lightweight local development story. It can run as a
container when Docker is available, but it can also consume generated file
configuration when a host-managed process is preferred.

Nginx and HAProxy are good later providers, especially for environments where
operators already standardize on them. Envoy is powerful but too complex as the
first default because its configuration and xDS model are a larger abstraction
surface than CloudShell needs for the first pass.

## Goals

- Model a load balancer as a resource, not as a special Control Plane service.
- Keep route intent provider-neutral: expose entrypoints, map HTTP host/path
  rules, map TCP ports, and point at stable backend targets.
- Prefer endpoint references as route targets. A raw port should be a
  convenience only when the target resource has not projected a named endpoint.
- Let the provider own runtime config, generated files, reloads, containers,
  TLS internals, health probes, and balancing algorithms.
- Support providers that run on different host runtime models, including but
  not limited to container-compatible hosts.
- Keep UI projection understandable: entrypoints, routes, targets, provider,
  and readiness.

## Non-Goals

- Do not standardize every provider-specific option in the first version.
- Do not require Docker to use load balancing.
- Do not make load-balanced routes point directly at every runtime replica.
- Do not introduce global DNS, certificate automation, WAF policy, or service
  mesh concepts as prerequisites.
- Do not make `Deployment` or container application environments prerequisites
  for the first load-balancer abstraction.

## Resource Model

A load balancer is a `Resource` with `ResourceClass.Network` and a precise type
such as `cloudshell.loadBalancer`.

It projects:

- exposed entrypoints as endpoints, such as `http`, `https`, and TCP ports
- route definitions as provider-neutral load-balancer metadata
- dependencies on target resources and the selected provider resource when one
  is explicit
- capabilities including `networking.provider`, `networking.gateway`,
  `networking.loadBalancer`, `networking.endpointMapper`, and optionally
  `networking.tls`
- reconcile/apply actions for generating or applying provider configuration

The load balancer resource is the stable user-facing abstraction. Provider
resources may be projected separately when the implementation is an activated
host service, container app, or external controller.

The load balancer resource does not become a container app just because the
selected provider uses containers. A provider may create, start, stop, replace,
and inspect implementation containers that belong to the load balancer. Those
runtime containers are provider-owned child resources or internal provider
state. They are useful for logs, health, diagnostics, and low-level operations,
but they are not the stable resource users target when they define routes.

## Host Selection

CloudShell should use one host-selection strategy across provider-owned
runtime infrastructure. Users select a host resource, not a container engine.
In this context, a host is an instance of a runtime or control boundary that
CloudShell can target. It is not necessarily a physical machine or VM. A host
can represent a Docker Engine, Podman machine, containerd endpoint, Kubernetes
cluster, Nomad cluster, systemd-capable node, VM boundary, or vendor appliance
API.

The recommended vocabulary is:

- container host: the selectable CloudShell resource or configured runtime
  instance
- container runtime: the implementation capability or product family available
  through that host
- engine: product-specific wording only, such as Docker Engine, or a temporary
  compatibility term for existing APIs

The host advertises runtime capabilities and provider-owned facts. Those facts
describe what the host can execute or control; they are not separate placement
primitives in the load-balancer model.

For local development, host selection can stay implicit and resolve to the
configured default or preferred host. For on-premise or multi-host
environments, the load balancer should be able to reference an explicit host
resource. That host may currently be projected by the Docker provider, but the
load balancer abstraction should not bind directly to a Docker engine concept.

This keeps three concepts separate:

- provider: the implementation kind, such as `traefik`
- host: the runtime/control instance where provider-owned infrastructure is
  materialized
- runtime capability: what the host can execute or control, described by host
  capabilities and provider-owned attributes

If the selected provider runs in a container, it creates provider-owned
runtime containers on the selected host and parents or tracks those containers
according to provider policy. If the selected provider is host-managed, the
same `HostResourceId` can identify the host service or appliance boundary.
If the selected provider runs through a scheduler or appliance, the host
represents that scheduler or appliance instance instead of a Docker-compatible
endpoint.

## Normalized Route Model

The fluent API should normalize into route definitions with this conceptual
shape:

```csharp
public sealed record LoadBalancerResourceDefinition(
    string Id,
    string Name,
    string Provider,
    string? HostResourceId,
    IReadOnlyList<LoadBalancerEntrypoint> Entrypoints,
    IReadOnlyList<LoadBalancerRoute> Routes);

public sealed record LoadBalancerEntrypoint(
    string Name,
    ResourceEndpointProtocol Protocol,
    int Port,
    ResourceExposureScope Exposure = ResourceExposureScope.Public);

public sealed record LoadBalancerRoute(
    string Id,
    string Name,
    LoadBalancerRouteKind Kind,
    string EntrypointName,
    LoadBalancerRouteMatch Match,
    LoadBalancerRouteTarget Target);

public enum LoadBalancerRouteKind
{
    Http,
    Tcp
}

public sealed record LoadBalancerRouteMatch(
    string? Host = null,
    string? PathPrefix = null,
    int? Port = null);

public sealed record LoadBalancerRouteTarget(
    string ResourceId,
    string? EndpointName = null,
    int? Port = null);
```

The target should prefer `EndpointName`. `Port` is a convenience for simple
authoring and should either resolve to an existing endpoint by port/protocol or
be passed to the provider as provider-owned target configuration.

## Fluent API

The user-facing API should support the concise shape:

```csharp
var lb = resources
    .AddLoadBalancer("load-balancer:public")
    .WithDisplayName("Public")
    .UseProvider("traefik")
    .UseContainerHost("docker:engine")
    .ExposeHttp(80)
    .ExposeHttps(443);

lb.MapHost("app.local", webApp, endpoint: "http");
lb.MapPath("api.local", "/v1", apiService, endpoint: "http");
lb.MapTcp(5432, postgresReplicaSet, endpoint: "postgres");
```

For cases where a resource has not projected a named endpoint yet, a port
overload can remain available:

```csharp
lb.MapHost("app.local", webApp, port: 8080);
lb.MapPath("api.local", "/v1", apiService, port: 5000);
lb.MapTcp(5432, postgresReplicaSet, port: 5432);
```

The semantically explicit shape should also be available for advanced callers:

```csharp
lb.MapHttp("app")
    .OnHost("app.local")
    .UseEntrypoint("http")
    .ToEndpoint(webApp, "http");

lb.MapHttp("api-v1")
    .OnHost("api.local")
    .OnPathPrefix("/v1")
    .UseEntrypoint("https")
    .ToEndpoint(apiService, "http");

lb.MapTcp("postgres")
    .OnPort(5432)
    .ToEndpoint(postgresReplicaSet, "postgres");
```

The concise API should compile into the explicit route model. The explicit API
is useful when a route needs a custom ID, entrypoint selection, future TLS
policy, or provider-owned options.

`UseContainerHost(...)` is optional. When omitted, provider execution should
use the environment's configured default or preferred container host. The
Resource Manager creation and configuration UI should prompt for this host when
more than one eligible host is available, using the same host list used by
container-backed resources and future remote-host support.

## Relationship to Virtual Networks

The load balancer can be used in two ways:

1. As a standalone network resource that exposes host-local or public
   entrypoints.
2. As the selected provider for a virtual network endpoint mapping.

For standalone use, the load balancer owns the exposed endpoints and route
definitions.

For virtual-network use, the virtual network can own the public endpoint while
the load balancer resource materializes the mapping:

```csharp
var vnet = resources
    .AddVirtualNetwork("network:app")
    .WithDisplayName("App Network");
var lb = resources
    .AddLoadBalancer("load-balancer:public")
    .WithDisplayName("Public")
    .UseProvider("traefik");

var publicApi = vnet.RequestHttpEndpoint(
    "api-public",
    host: "api.local",
    exposure: ResourceExposureScope.Public);

vnet.MapEndpoint(
    publicApi,
    new ResourceEndpointReference(apiService.ResourceId, "http"),
    lb,
    "mapping:api-public");
```

The load-balancer-specific route API should be the more ergonomic authoring
surface for host/path/TCP routing. Endpoint mappings remain the lower-level
resource relationship that the Control Plane can validate and display.

## Relationship to Orchestrator Services

Standalone load-balancer routes can target ordinary resources and endpoint
references directly. When a target is an orchestrated workload, such as a
container app with a replica group, the load balancer should not own replica
enumeration or infer membership from Kubernetes-style labels. The orchestrator
owns the service and replica-group boundary and should provide an explicit
routing binding that says which CloudShell service id, replica group id, route
or mapping id, and endpoint reference are currently active.

The load balancer provider reacts to that binding by materializing provider
configuration for the selected implementation. On scale-up, scale-down, image
replacement, or replica-group cleanup, the default orchestrator updates the
binding as part of deployment or replica-group reconciliation. Provider
adapters can translate the binding to labels, backend pools, generated files,
service discovery records, or native load-balancer APIs as needed, but those
translation details remain provider-owned.

## Provider Responsibilities

A load balancer provider must:

1. Project or activate a provider resource with `networking.loadBalancer`.
2. Validate that each target resource exists.
3. Prefer target endpoint references and validate that the endpoint exists
   when the route uses `EndpointName`.
4. Resolve port-only targets according to provider rules.
5. Resolve the selected host, defaulting through CloudShell's configured host
   preference when `HostResourceId` is omitted.
6. Validate that the host can run or reach the selected provider mode.
7. Materialize route configuration for the selected implementation.
8. Report action capability reasons when a provider cannot apply a route.

For Traefik, the first implementation should generate dynamic configuration:

- HTTP routers for host and path-prefix rules.
- TCP routers for port-based routes.
- Services pointing at target endpoint addresses or provider-resolved ports.
- Entrypoints for exposed HTTP, HTTPS, and TCP ports.

The initial Traefik provider can run in one of two modes:

- file-config mode, where CloudShell writes dynamic configuration for an
  existing Traefik process or host service
- container mode, where the Traefik provider creates and manages its own
  runtime container resources on the selected host when that host advertises a
  compatible container runtime capability
- orchestrated or appliance mode later, where the selected host represents a
  scheduler, platform, or appliance API rather than a Docker-compatible engine

The same load balancer resource model should work for both modes.

In container mode, the provider-owned container lifecycle should be tied to the
load balancer resource lifecycle. Starting the load balancer can start or create
the implementation container; stopping it can stop that container; deleting the
load balancer can remove provider-owned runtime state according to provider
policy. The user should not need to model that implementation container as a
separate container app unless they explicitly want to manage Traefik as an
ordinary workload.

## UI Projection

Resource Manager should show:

- provider, such as `traefik`
- exposed entrypoints and ports
- HTTP routes grouped by host and path
- TCP routes grouped by port
- target resource and endpoint/port
- readiness and missing-provider warnings
- reconcile/apply action and action capability reasons

Target resources should show read-only network exposure when a load-balancer
route points at one of their endpoints, similar to virtual network endpoint
mappings.

## Open Questions

- Should `UseProvider("traefik")` select a provider kind, a provider resource,
  or both?
- Should the first implementation create a provider resource automatically, or
  require an explicit `resources.AddTraefikLoadBalancerProvider(...)`?
- What host capability vocabulary should CloudShell standardize first, and
  which runtime facts should remain provider-owned attributes?
- Which provider-owned runtime containers should be projected as child
  resources, and which should remain internal provider state?
- Should future TLS options remain entrypoint-level, move some policy to
  route-level, or split issuer/renewal behavior into provider-owned
  configuration? The first implementation uses entrypoint-level Secrets Vault
  certificate references.
- Should DNS hostnames such as `app.local` become resource references to a DNS
  provider later?
- How should route conflicts be reported when two load balancers claim the same
  host/path or TCP port?
- Should health probes be standardized on load-balancer targets in the first
  implementation, or stay provider-owned until backend pools are implemented?

## Implementation Plan

1. Add load-balancer route and entrypoint definitions to the resource model.
2. Add `AddLoadBalancer(...)` and fluent route builders.
3. Add `HostResourceId` and `UseContainerHost(...)` so provider-owned runtime
   infrastructure is placed on container hosts rather than engines.
4. Project `cloudshell.loadBalancer` resources from the platform provider.
5. Add declaration tests for provider selection, host selection, entrypoints,
   HTTP host/path routes, TCP routes, dependencies, and capabilities.
6. Add Resource Manager generated UI support for entrypoints and routes.
7. Add a Traefik provider in file-config mode first.
8. Add optional Traefik container mode where the load-balancer provider creates
   and owns implementation containers as child/runtime resources.
9. Add a sample with a web app, API service, PostgreSQL-style TCP target, and a
   public Traefik load balancer.

## Remaining tasks

- Finish the provider-resource selection path so `UseProvider(...)` and host
  resolution behave consistently across declared and UI-created resources.
- Complete Traefik diagnostics for provider capability gaps, runtime probes,
  and provider-owned configuration failures. Generated Resource Manager
  diagnostics now cover missing selected host resources and missing route
  target resources/endpoints. File-config apply is in place; managed container
  mode now uses load-balancer Start/Stop and Delete cleanup rather than
  starting as a side effect of Apply.
- Add richer backend target resolution and TLS lifecycle coverage before the
  design is considered production-ready. In-resource route-shape, duplicate
  route-match, duplicate entrypoint, host-port conflict, and HTTPS certificate
  reference validation already exist for load-balancer setup, and Traefik can
  materialize vault-backed PEM certificates for HTTPS entrypoints.
