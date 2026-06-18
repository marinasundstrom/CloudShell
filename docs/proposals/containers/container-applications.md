# Container Applications Proposal

## Status

In progress.

Container applications are the MVP's primary managed-service resource. They are
the stable user-facing deployment, configuration, scaling, exposure, identity,
storage, and observability surface for containerized application workloads.

This proposal tracks the container app resource itself. Related proposals own
adjacent subdomains:

* [Container host abstraction](container-host-abstraction.md) owns host
  selection and provider-owned runtime placement.
* [Deployments and revisions](../deployment/deployments-and-revisions.md) owns
  rich rollout history and orchestrator deployment/revision semantics.
* [Virtual network resource](../networking/virtual-network-resource.md),
  [Load balancer resource](../networking/load-balancer-resource.md), and
  [DNS and name mapping](../networking/dns-and-name-mapping-resource.md) own
  network exposure and name publication primitives.
* [Storage and volume mappings](../storage/volume-mappings.md) owns volume
  resources and mount compatibility.
* [Identity and access](../core/identity-and-access.md) owns resource identity,
  credential delivery, and permission enforcement.

## Problem

CloudShell has several resource types that can run application code:
executables, ASP.NET Core projects, Docker child containers, SQL Server, and
container apps. Without a dedicated container application model, users and
providers can accidentally treat runtime containers, Docker Compose services,
Kubernetes Services, or optional `cloudshell.service` resources as the stable
managed service.

That blurs important boundaries:

* A runtime container is an implementation detail that may be replaced.
* A container host is the placement/control boundary, not the app.
* An orchestrator service descriptor groups replicas for provider execution,
  but it is not a normal Resource Manager resource by default.
* A `cloudshell.service` resource is optional for deliberate facades, imported
  services, non-application targets, or advanced routing. It should not be
  required for normal container app exposure.
* Public endpoints, virtual-network exposure, load-balancer routes, internal
  DNS-style names, and custom domains should be understandable from the
  application resource configuration experience.

CloudShell needs one clear managed-service surface for containerized workloads,
similar in spirit to Azure Container Apps, while keeping CloudShell's resource
model provider-neutral and self-hosted.

## Goals

* Make `application.container-app` the stable managed-service resource for
  containerized application workloads.
* Keep runtime containers, provider-native service objects, and orchestrator
  service descriptors as provider/runtime implementation details unless a
  provider explicitly projects them for inspection.
* Let a container app own image, registry, current revision, opt-in scaling
  with replicas, endpoints, service discovery intent, exposure intent,
  identity binding, volume mounts, environment variables, observability, and
  lifecycle actions.
* Support internal exposure through host-local networking or virtual networks.
* Support public endpoint exposure through app-owned ingress, public endpoints,
  load balancers, or explicitly modeled service facades when needed.
* Support DNS-style internal names and custom domain mappings as relationships
  to the container app endpoint.
* Keep ASP.NET Core project containerization as a conversion path into a
  container app, not as a separate runtime model.
* Keep the UI management path application-centric so users can configure,
  inspect, and operate a managed service without understanding the underlying
  host or orchestrator artifacts first.
* Keep provider-owned configuration and credentials behind provider contracts;
  project only stable non-secret facts as resource attributes.

## Non-Goals

* Do not make Docker containers the stable deployment target.
* Do not require `cloudshell.service` for normal container app exposure.
* Do not standardize Kubernetes, Docker Compose, or Docker implementation
  details in the public container app resource model.
* Do not implement autoscaling, traffic splitting, blue/green rollout, or rich
  revision history in the MVP.
* Do not make public DNS propagation, certificate automation, or provider-backed
  service registries part of the first container app slice.
* Do not make every local-development app use containers. Executables and
  ASP.NET Core project resources remain valid development-time resources.

## Resource Model

The stable projected resource uses:

```text
TypeId: application.container-app
ResourceClass: Application
```

The resource may project:

* `container.image`
* `container.registry`
* `container.revision`
* `container.replicas.enabled`
* `container.replicas`
* endpoint count and endpoint metadata
* selected container host or default-host intent
* volume mount count
* app-owned ingress or exposure relationships
* observability settings
* identity binding
* lifecycle actions and action capability reasons

The app may produce an orchestrator-facing service descriptor for replicas,
ports, networks, dependencies, and provider-owned ingress. That descriptor is
not a Resource Manager resource by default. Runtime containers or replicas may
be projected as child resources for diagnostics by a host provider, but image
updates, replica updates, lifecycle actions, storage, identity, and exposure
configuration should target the container app resource.

## Managed-Service Configuration Surface

Resource Manager should treat the container app as the main configuration
surface for:

* image and registry
* current revision and image deployment
* scaling mode and replica count
* endpoints
* Aspire-compatible developer service discovery references
* environment variables and app settings
* secrets/configuration references
* identity binding and permission grants
* storage mounts
* internal exposure on the host network or virtual networks
* public endpoint and load-balancer exposure
* DNS-style internal names and custom domain mappings
* app-scoped logs, structured logs, traces, telemetry metrics, and activity
  events
* resource monitoring summaries and per-replica runtime metrics

Related resources should still be visible and navigable. A load balancer,
virtual network, volume, DNS zone, or name mapping remains its own resource
when it has independent lifecycle, provider configuration, diagnostics, or
authorization. The application overview should also show inbound relationships
so users can answer "how is this app exposed?" from the app page.

## Current Implementation

Implemented pieces include:

* programmatic `AddContainerApplication(...)` and Aspire-compatible
  `AddContainer(...)` declarations
* Resource Manager registration and configuration UI for container apps
* image, registry, environment-variable, endpoint, lifetime, replica, and
  container-host configuration, including create and update host selection
* `AsContainer(...)` conversion for ASP.NET Core project resources
* current revision projection when a container app image is updated
* Resource Manager Deployment tab with image update action
* Resource Manager Application > Scale and replicas tab for enabling replicas
  and setting desired replica count
* app-owned internal deployment projection with status, service id, workload
  version, desired replicas, and projected runtime replicas. This is the
  container app use of the broader default-deployment rule: a resource remains
  directly manageable while the orchestrator derives a deployment for
  deployment-relevant changes.
* explicit replica count update API that also opts a container app into
  replica mode
* shared resource metadata for provider/orchestrator/runtime ownership,
  visibility, owner resource, and cleanup behavior
* internal orchestrator deployment/revision data contracts for future
  container app runtime materialization
* hidden runtime-managed child resources for container app replica/container
  projections, parented to and owned by the stable container app resource,
  with deployment/service/revision correlation metadata
* app-scoped Scale and replicas diagnostics that list projected runtime
  replicas without requiring global hidden/runtime-managed inventory settings;
  single-instance apps explain that replicas are not enabled instead of
  projecting a single-instance container as a replica set
* app-scoped Monitoring tab under Management that summarizes single-instance
  container stats and replicated app resource usage from projected
  replica/container monitoring snapshots when a static/default container host
  can be resolved
* application-level service discovery opt-in through `WithServiceDiscovery()`
* volume mount model and Storage tab for resources that support storage
* identity binding and standard runtime credential delivery path
* observability environment variable projection
* structured logs and trace views for application diagnostics
* app-scoped telemetry design for multi-replica container apps, where
  resource Telemetry views default to all runtime instances and later expose a
  runtime scope selector only when multiple instances exist
* app-owned ingress for replicated Docker-backed apps
* inbound virtual-network, load-balancer, and DNS/name-mapping relationship
  display on application overview pages
* app-centric load-balancer creation from container-backed application overview
  pages through a prefilled Resource Manager create flow with the target app
  endpoint selected
* app-centric name-mapping creation from container-backed application overview
  pages through a prefilled Resource Manager create flow
* attached volume display on application overview pages, with the Storage tab
  remaining the edit surface
* local host-published endpoint preflight before container app start
* declared Docker container Start preflight for occupied local TCP/HTTP
  endpoint ports, covering local registry resources used by container app
  deployment samples
* local/default container-host path, host capability diagnostics, and
  application overview host placement/readiness display

## MVP Implementation Plan

1. Keep the application resource as the default management entry point. Done
   for overview, configuration, storage, activity, logs, traces, exposure
   relationships, and attached volume visibility.
2. Keep container host selection explicit or defaulted through the host
   resolver. Initial resolver, create/update host selection, and missing-host
   diagnostics exist. Application overview pages now show resolved host status,
   endpoint, registry, credentials availability, and advertised capabilities;
   deeper host readiness diagnostics continue in the host proposal.
3. Materialize volume mounts in the runtime providers and surface mount
   compatibility diagnostics from host/storage medium capabilities.
4. Finish the Resource Manager exposure path from the application resource:
   endpoint, internal/virtual exposure, public endpoint, load balancer, and
   DNS/domain mappings. The first app-centric load-balancer and DNS/name
   authoring slices are in place through prefilled Resource Manager create
   flows.
5. Add conflict and readiness diagnostics for endpoint ports, load-balancer
   routes, DNS/name mappings, and unsupported host capabilities. Local
   host-published endpoint preflight is in place for container app start;
   route, DNS, and provider-backed diagnostics remain open.
6. Keep image update, current revision, explicit replica scaling, and hidden
   runtime ownership metadata as the MVP deployment surface. Container apps
   default to single-instance mode; enabling replicas is a deliberate
   Application > Scale and replicas action or programmatic `WithReplicas(...)`
   declaration. Scale and replicas now prompts endpoint-bearing apps to create
   a load-balancer route when replicas are enabled. Use the internal
   orchestrator deployment/revision contracts for container app implementation
   work, but defer public rollout history, rollback, revision management, and
   traffic splitting to later deployment/revision slices.
7. Keep container app replica diagnostics app-scoped in Scale and replicas. It
   shows app-owned replica/runtime diagnostics to users who can view or manage
   the container app without requiring the global runtime-managed inventory
   view; the global `Show runtime-managed resources` setting remains for
   browsing hidden runtime-managed artifacts directly in the resource
   inventory.
8. Add an app-scoped Monitoring tab for container apps that summarizes
   provider-observed resource metrics for the app and shows each projected
   runtime replica/container separately. This should use the resource
   Monitoring menu item under Management, not the shared Telemetry metrics
   surface, because CPU, memory, network, block I/O, process count, restart,
   uptime, and provider materialization state are resource metrics.
9. Validate the managed-service story with samples that combine container app,
   storage, service discovery, identity, secrets/configuration, logs, traces,
   and public/name exposure.

## Remaining Tasks

* Materialize container app volume mounts reliably through the supported local
  runtime paths.
* Add application-centric UI for internal exposure and public endpoint
  exposure. Load-balancer and DNS/domain mapping authoring now have first
  app-centric entry points, but route editing on existing load balancers,
  richer provider-specific publishing, and custom domain guidance remain open.
* Add route, DNS/name, and provider-backed endpoint conflict diagnostics before
  start/update where possible.
* Add host capability diagnostics for unsupported storage media, ingress,
  public endpoint, or DNS/name publication choices.
* Add deeper app-owned ingress/provider guidance after the first Scale and
  replicas load-balancer prompt. The endpoint remains owned by the container app: a
  single container binds it in single-instance mode, and an ingress or load
  balancer binds it on behalf of the app in replicated mode. Worker-style
  replicated apps without inbound endpoints should not require a load balancer.
* Enrich hidden replica/container child resources with provider-observed
  container IDs, health, placement, and materialization state once providers
  can report them consistently.
* Enrich Scale and replicas with provider-observed container IDs, placement,
  health, and materialization state once providers can report them
  consistently.
* Enrich the provider-owned Monitoring dashboard for container apps with
  provider-observed container IDs, placement, health, restart count, uptime,
  and materialization diagnostics once providers report them consistently.
* Add deeper container-host readiness diagnostics for unsupported ingress,
  public endpoint, DNS/name publication, registry credential, and storage
  choices before update/start.
* Add runtime scope dimensions to container app logs, traces, and telemetry
  metrics so Resource Manager can offer an `All instances` default plus
  per-instance scope filtering when a replicated app has multiple runtime
  instances. Single-instance apps should not show a selector.
* Keep local container-registry configuration explicit so CloudShell does not
  assume `localhost:5000`. The Container App Deployment sample already uses an
  explicit non-default port, and declared Docker container resources now
  preflight occupied local TCP/HTTP endpoint ports before Start. Future
  registry-backed providers should also suggest or allocate alternate ports
  when a default is unavailable.
* Improve restart/update behavior around image, replica, environment, endpoint,
  identity, and storage changes.
* Keep supported samples green with a broad container app scenario that uses
  SQL Server, mounted storage, service discovery, secrets/configuration,
  identity, structured logs, traces, and name/public exposure.
* Decide when runtime container child resources should be projected for
  diagnostics without making them the stable deployment target.

## Open Questions

* How much of the exposure authoring path should live directly on the
  application configuration page versus dedicated network/load-balancer/DNS
  resource pages?
* Which endpoint and DNS conflicts can the Control Plane validate without a
  provider-specific preflight?
* Should internal DNS-style names and custom domains share one UI flow with an
  exposure-scope selector, or should custom domains get a specialized flow once
  TLS/certificate automation exists?
* What is the smallest useful revision history that belongs to the container
  app before the richer orchestrator deployment/revision model lands?
* Which runtime scope dimension names should become stable telemetry contract
  fields versus provider-owned attributes on logs, spans, and metric points?
