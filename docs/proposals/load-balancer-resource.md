# Load Balancer Resource Proposal

## Status

Proposed.

## Problem

CloudShell can now model networks, endpoints, endpoint requests, endpoint
mappings, and provider-backed host networking. The next step is a load balancer
resource that gives users a stable, provider-neutral way to expose HTTP, HTTPS,
and TCP traffic while allowing the concrete implementation to be Traefik,
Nginx, HAProxy, Envoy, a cloud load balancer, or a custom provider.

The abstraction should not assume that the provider runs in Docker. A load
balancer provider may be:

- a container app managed by CloudShell
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
- Support providers that run in Docker and providers that do not.
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

## Normalized Route Model

The fluent API should normalize into route definitions with this conceptual
shape:

```csharp
public sealed record LoadBalancerResourceDefinition(
    string Id,
    string Name,
    string Provider,
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
var lb = resources.AddLoadBalancer("load-balancer:public", "Public")
    .UseProvider("traefik")
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
var vnet = resources.AddVirtualNetwork("network:app", "App Network");
var lb = resources.AddLoadBalancer("load-balancer:public", "Public")
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

## Provider Responsibilities

A load balancer provider must:

1. Project or activate a provider resource with `networking.loadBalancer`.
2. Validate that each target resource exists.
3. Prefer target endpoint references and validate that the endpoint exists
   when the route uses `EndpointName`.
4. Resolve port-only targets according to provider rules.
5. Materialize route configuration for the selected implementation.
6. Report action capability reasons when a provider cannot apply a route.

For Traefik, the first implementation should generate dynamic configuration:

- HTTP routers for host and path-prefix rules.
- TCP routers for port-based routes.
- Services pointing at target endpoint addresses or provider-resolved ports.
- Entrypoints for exposed HTTP, HTTPS, and TCP ports.

The initial Traefik provider can run in one of two modes:

- file-config mode, where CloudShell writes dynamic configuration for an
  existing Traefik process or host service
- container mode, where CloudShell runs Traefik as a container app when Docker
  or another container provider is available

The same load balancer resource model should work for both modes.

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
- Should load balancer routes be projected through a first-class
  `ResourceLoadBalancerRoutes` property or provider-neutral attributes until
  the model stabilizes?
- How should TLS be represented: entrypoint-level, route-level, certificate
  resource reference, or provider-owned configuration?
- Should DNS hostnames such as `app.local` become resource references to a DNS
  provider later?
- How should route conflicts be reported when two load balancers claim the same
  host/path or TCP port?
- Should health probes be standardized on load-balancer targets in the first
  implementation, or stay provider-owned until backend pools are implemented?

## Implementation Plan

1. Add load-balancer route and entrypoint definitions to the resource model.
2. Add `AddLoadBalancer(...)` and fluent route builders.
3. Project `cloudshell.loadBalancer` resources from the platform provider.
4. Add declaration tests for provider selection, entrypoints, HTTP host/path
   routes, TCP routes, dependencies, and capabilities.
5. Add Resource Manager generated UI support for entrypoints and routes.
6. Add a Traefik provider in file-config mode first.
7. Add optional Traefik container mode after the container-provider lifecycle is
   ready for provider-managed infrastructure resources.
8. Add a sample with a web app, API service, PostgreSQL-style TCP target, and a
   public Traefik load balancer.
