# Container Applications Proposal

## Status

In progress.

Container applications are the MVP's primary managed-service resource. They are
the stable user-facing deployment, configuration, scaling, exposure, identity,
storage, and observability surface for containerized application workloads.
Most CloudShell resources represent things that exist in an environment or
configuration that should be available to workloads. Container apps are
different: they are workload resources that intentionally change the hosting
environment by asking Resource Manager orchestration to deploy and reconcile
runtime resources such as services, replica groups, routing, and runtime
containers.

That makes a container app a managed workload facade, not just a one-to-one
resource projection. The container app still exists as a normal Resource
Manager resource: it has a stable identity, resource definition, lifecycle
actions, update surface, permissions, logs, health, and UI pages. What makes it
different is that it is the user-facing interface for a related set of
container-app-owned runtime and orchestration artifacts. Resource Manager and
the provider can create, update, retain, drain, or delete orchestrator services,
replica groups, runtime replicas, endpoint mappings, load-balancer bindings,
deployment records, and app revision history below that stable resource. Users
operate the container app; the platform reconciles the contained runtime shape.
Other resource types may have lifecycle operations or provider-owned runtime
state, but most do not need this deployment-centric set of versioned related
resources.

The current implementation direction is to finish moving container apps onto
the Resource Model POC path. User-facing creation and updates should be
expressed as full or incremental `ResourceDefinition` entries: image, replica
slots, endpoints, volume mounts, references, and other app-owned intent. After
Resource Manager accepts that graph state, the container app provider should
produce internal deployment-planning input for the orchestrator. Runtime
replica creation, scale-in, scale-out, readiness, routing rebinding,
load-balancer/ingress updates, retained previous slots, and cleanup should be
handled by the orchestrator/controller path. Separate provider-specific paths
that directly remove all replicas, recreate ingress, or remap backends during
image and replica updates are temporary POC seams to remove.

After the first resource-definition readability cleanup removes unnecessary
language wrapper groups, a later container-app authoring slice should revisit
the container group. The desired authored shape is intentionally closer to a
Tye-style service declaration:

```yaml
resources:
  - type: application.container-app
    name: api
    dependsOn:
      - resourceId: docker.host:local
        typeId: docker.host
    image: ghcr.io/acme/api:dev
    registry: ghcr.io
    replicas: 2
    endpoints:
      - name: http
        protocol: http
        targetPort: 8080
        port: 5080
        exposure: Public
    routing:
      sessionAffinity:
        mode: None
```

`endpoints` is the preferred authored name for the existing endpoint-request
semantics. It should resolve to the same provider intent as today's
`container.endpointRequests`; only the authoring path should become more
readable. This should be a separate migration because `container` is currently
a meaningful domain group, and removing it needs a deliberate review of which
container fields should become root fields and which should remain grouped.

This proposal tracks the container app resource itself. Related proposals own
adjacent subdomains:

* [Container host abstraction](container-host-abstraction.md) owns host
  selection and provider-owned runtime placement.
* [Orchestrator deployments and environment revisions](../deployment/deployments-and-revisions.md)
  owns the generalized Resource Manager orchestration deployment/revision
  model. Container apps are the first feature using that model, not the feature
  that owns it. The same model should remain available for other workload
  resources that need versioned runtime materialization later.
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

The container app resource provider is the Resource Manager integration for
`application.container-app`. It defines the lifecycle actions, action
availability, validation, app-configuration updates, deployment description,
and provider-specific runtime commands for the resource type. Shared
application services can provide reusable support for process/container
spawning, runtime state tracking, logs, environment-variable resolution, and
projection helpers across executable apps, ASP.NET Core projects, SQL Server,
and container apps. Those services are implementation support; they should not
be the owner of Resource Manager deployment, revision, lifecycle, or replica
group semantics.

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
  revision history in the MVP. Image deployment should still move toward the
  revision-based deployment model instead of being treated as an in-place
  resource restart.
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

The app may produce an orchestrator-facing desired runtime state: a stable
service, routing or load-balancer configuration for that service, and a
replica group of N runtime resource instances for image X. That descriptor is
not a Resource Manager resource by default. It is the container app's request
for the orchestrator to manage CloudShell runtime resources and configuration
for the app. After apply, the orchestrator records an environment revision and
returns materialization data such as the replica group. The container app can
correlate its app revision to that orchestrator deployment or environment
revision, especially when projecting runtime replica resources, but it does not
share revision identity with the orchestrator.

The current container app to runtime mapping diagram is maintained in
[Container Apps](../../resources/container-apps.md).

The stable app resource owns the user-facing app configuration and app
configuration revisions. The orchestrator deployment is the translated desired
runtime state for that app revision. The orchestrator service, routing, replica
group, slots, and runtime resource occupants are the materialized runtime
shape. Environment revisions record the materialized hosting-environment
outcome; they are traceable from the container app revision but do not share
identity with it.
Starting a deployment-capable container app should establish the initial
orchestrator deployment baseline and environment revision for the materialized
runtime state. Later image and replica changes can then compare against the
active deployment record instead of inferring predecessor runtime resources
from provider-specific names or projected child resources.

Deployment apply is incremental: the requested runtime state creates or updates
specified resources by id, and removal remains an explicit scale-down,
revision-retirement, or service tear-down operation. Runtime containers or
replicas may be projected as child resources for diagnostics by a host
provider, but image updates, replica updates, lifecycle actions, storage,
identity, and exposure configuration should target the container app resource.

Revision-scoped container app replicas should be tracked as a group within the
orchestrator service boundary. That group is what lets the orchestrator
understand which runtime replicas belong to the current revision, which belong
to a candidate or superseded revision, and which replicas should participate in
routing, diagnostics, readiness, drain, and cleanup. Container apps should
therefore consume the orchestrator-managed replica group as the default
replication model instead of owning replica enumeration in provider-specific
helpers. The provider can still execute member-level Docker commands where the
default local runner requires them. New image deployments can create a
replacement replica group and cut service routing over to it, while scale-only
updates can reconcile the current group by adding or removing member resources.
The group change calculation belongs to the orchestrator abstraction so the
container app provider does not have to own replica-count diffing itself.

The container app should define the replica group as part of the orchestrator
deployment. In Kubernetes terms this is closest to a replica set, but
CloudShell keeps it resource-centered: the group carries replica attributes,
including requested replica slots and lifecycle request, and a replica member
resource definition that describes the runtime resource shape for each app
replica. That member definition includes image, command, environment, endpoint
bindings, mounts, identity, and resource correlation metadata. A changed member
definition creates a new versioned replica group. A requested slot-count
change against the same member definition reconciles membership in the existing
group and records a new Environment revision for the capacity change.

Container apps should treat replica groups as a set of requested **replica
slots**. A slot is the desired position in the group; the container resource is
the current occupant of that slot. When a container crashes or disappears, the
orchestrator should evaluate the slot rather than immediately treating the
whole container app as stopped. If other slots are still serving, the app may
be degraded while the slot policy decides whether to fill the vacant or failed
slot.

A container app deployment that asks for a replica group asks for **requested
replica slots**. That request is not a best-effort desired count: if the
orchestrator cannot materialize the requested slots because of provider
failure, placement limits, host capacity, or configuration policy, the
deployment fails and the previous active runtime state remains authoritative.
Once a group has been materialized, the app can distinguish requested slots,
materialized slots, and occupied replica count so a missing occupant is
handled as slot reconciliation rather than as the whole app stopping.

The first container app policy should stay slot-focused:

* A slot can be left vacant when automatic repair is disabled.
* A slot can restart the same occupant when the runtime identity still exists
  and retrying the same container is explicitly requested.
* A slot can be filled by replacement, where the failed occupant is removed if
  necessary and a new container is materialized for the same slot. This is the
  expected default for container app replicas because containers are cheap and
  should be disposable.

This is distinct from general resource recovery. Resource recovery decides
whether the stable container app resource should be recovered as a management
unit. Replica slot management decides how the orchestrator maintains requested
capacity inside an already-active replica group. The container app UI can later
surface this as replica behavior or slot repair policy without requiring users
to understand environment revisions, deployment objects, or provider-native
container restart settings up front.

The component that keeps container app replica slots aligned should be the
Resource Manager orchestration reconciler. The container app provider declares
the app configuration, projects runtime replica resources, contributes
liveness signals, and executes provider-specific Docker/container operations
when asked. It should not run an independent loop that decides how many
replicas should exist or whether a failed slot should restart or be replaced.
That decision belongs to the replica group reconciler because it has the active
deployment, replica group, slot policy, liveness observations, and environment
revision context.

Likewise, service routing should be reconciled from replica-group state. A
load balancer or ingress provider should rebind to the target replica group
through an orchestrator/controller routing hook instead of requiring the
container app image-update or replica-update operation to know how backends are
registered. Scale-out can add replicas before routing changes when policy
requires readiness first. Scale-in can remove routing membership before
stopping stale replicas when policy requires draining. Image replacement can
materialize the new group, rebind, then retire the previous group according to
retention and cleanup policy.

Liveness evaluation should observe and record unhealthy or vacant replica
slots, then hand those observations to replica management. Health refresh must
not synchronously own replacement work, rebuild images, or rerun full service
preparation. A separate replica group reconciliation service can process the
observations, coalesce repeated reports for the same slot, and invoke the
provider with the narrower intent of restarting or replacing the slot occupant.
The service should also record slot runtime state separately from the queued
work item so the app can show when a requested slot is unhealthy, repairing,
repaired, or repair-failed even after the queue has been drained.

Container app activity should surface replica management events without making
the user inspect orchestrator internals first. The app should show that a
replica slot became vacant or unhealthy, which policy decision was selected,
whether a restart or replacement was attempted, and whether the slot was filled
or intentionally left vacant. The Environment view can expose the same events
with deployment, Environment revision, replica group, slot, and occupant
correlation for debugging.

The Scale and replicas view should be slot-first. The primary rows represent
requested replica slots, not only currently materialized runtime resources.
Each row can then show the current occupant when one exists: runtime resource
id, container name, revision, health, host, materialization state, and
management activity. A slot without an occupant should remain visible as
vacant. The page should poll while open so users can see a slot move from
unhealthy, to replacing, to occupied again without manually refreshing.

## Container App Deployments and Configuration Revisions

Container app revisions are configuration-management snapshots owned by the
container app domain. They are not orchestrator revisions and they do not share
identity with orchestrator environment revisions.

For container apps, these configuration revisions are first-class because they
describe the app's configuration state and history. They are the records a user
inspects when asking what changed in the app configuration or which known-good
configuration can be used as the base for a restore.

A container app deployment records a user, build, or provider request to change
the app. It can create a new app revision, restore configuration from an old
app revision into a new app revision, or update app-owned operational history.
The app revision captures the configuration that defines app state:

* image and registry reference
* requested replica count for the deployment
* environment and command settings
* resource references and service discovery intent
* ingress, endpoint, and exposure intent
* identity, storage, and other resource configuration that affects runtime
  materialization
* the app revision it was based on, when the deployment starts from an older
  configuration snapshot
* the actor that provisioned the deployment

When runtime reconciliation is required, the container app provider translates
the selected app revision into an orchestrator deployment. The orchestrator
applies desired runtime state and records an environment revision describing
what changed in the hosting environment. The app revision may reference the
orchestrator deployment or environment revision for traceability, and the
orchestrator environment revision may reference the source container app
deployment, but the records remain separate.

Updating the image for a running container app should not be modeled as
"restart this resource." It should create a new container app configuration
revision for the requested image, optionally with a new requested replica
count, and then ask Resource Manager orchestration to materialize that
configuration. The orchestrator starts new runtime resources next to the
currently serving resources when availability requires it, switches traffic or
endpoint routing once the new runtime state is ready, and retires the old
runtime resources as a separate tear-down operation. Scaling the currently
active app revision is capacity management and does not necessarily create
another app revision.

Provider operation results should use a runtime reconciliation follow-up when
the app intent has changed and Resource Manager must apply a deployment. A
restart-required follow-up should be reserved for configuration edits that
cannot affect the running app until the user restarts the resource.

Explicit replica scaling should be consistent with the orchestration model:
the container app requests a deployment that changes desired runtime capacity
for the currently active app revision, and the orchestrator materializes that
change by starting or stopping replica group members. A successful scale
operation produces an orchestrator Environment revision because the hosting
environment changed, but it should not create a new container app
configuration revision when only the operational requested replica count is
changed. A new container app revision is appropriate when the deployment
changes the app configuration that should be versioned, such as image,
environment, command, endpoint, storage, identity, or a persisted scaling
policy/template. Manual requested-capacity changes remain deployment history
and environment history, not app configuration history by default.

When the app requests replicas, the requested lifecycle state of those runtime
members is part of the orchestrator deployment state. For example, a scale-up
deployment can request additional replica members in the started state, while a
scale-down or tear-down operation can request removed members to drain, stop,
or be deleted according to policy. The orchestrator validates and materializes
that request, then records the outcome in the Environment revision. The app
should not model this as manually creating replicas and then issuing separate
start commands.

Restoring a container app revision means creating a new deployment whose
requested app configuration is based on the configuration captured by an
existing app revision. It does not reactivate or mutate the old revision
object. The successful restore produces a new app revision that records the
based-on app revision relationship:

```text
App revision app-r12: known-good image and network config
    |
App deployment app-d13: image update with bad config
    |
App revision app-r13: failed or unhealthy desired app state
    |
App deployment app-d14: restore config from app-r12
    |
App revision app-r14: new active app configuration, basedOnRevisionId = app-r12
    |
Orchestrator deployment orch-d27: materialize app-r14 runtime state
    |
Orchestrator environment revision env-r27: hosting-environment change
```

The restore deployment may use the selected app revision configuration exactly,
or it may start from that configuration and include additional deployment
input before execution. The resulting app revision answers "which app
configuration state was this based on?" The resulting orchestrator environment
revision answers "which hosting-environment change materialized this
deployment?"

Future merge workflows should also be app-domain authoring operations. A merge
could create a new app revision from selected configuration fragments, such as
network settings from one app revision, image or environment state from
another, and replica settings from the current active app revision. The
selected app revisions are inputs to authoring a new deployment; they are not
mutated and they are not applied directly.

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

When a container app is implemented by replicas, Resource Manager should keep
users on the stable container app by default. Runtime replicas remain contained
resources for diagnostics and provider correlation, but app-scoped Logs,
Traces, Metrics, Monitoring, and Health views should list or filter the
replica-owned signals under the container app instead of requiring users to
navigate into each hidden replica. This containment presentation should be
generic enough to apply to future service-like grouping resources when they
own runtime children.

Related resources should still be visible and navigable. A load balancer,
virtual network, volume, DNS zone, or name mapping remains its own resource
when it has independent lifecycle, provider configuration, diagnostics, or
authorization. The application overview should also show inbound relationships
so users can answer "how is this app exposed?" from the app page.

## Current Implementation

The landed container app behavior is documented in
[Container Apps](../../resources/container-apps.md),
[Application resources](../../resources/application-resources.md),
[Resource Monitoring and Usage](../../monitoring-and-usage.md), and the
related networking and load-balancer feature docs.

Automatic scaling policies remain deferred. The current model supports manual
requested replica count changes. A future scaling policy should be declared on
the container app and materialized by an orchestrator or provider using load,
capacity, schedule, or provider-owned signals. That policy is separate from
resource recovery: recovery restores a failed or degraded workload from
liveness/lifecycle signals, while scaling changes desired capacity.

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
6. Keep container app deployment, current revision, explicit replica scaling,
   and hidden runtime ownership metadata as the MVP deployment surface.
   Container apps default to single-instance mode; enabling replicas is a
   deliberate Application > Scale and replicas action or programmatic
   `WithReplicas(...)` declaration. Scale and replicas now prompts
   endpoint-bearing apps to create a load-balancer route when replicas are
   enabled. Treat container app deployment as the bridge to orchestrator
   deployments and environment revisions: the app records the app-owned
   revision and requested replica count, then asks the orchestrator to
   materialize it. Starting replacement replicas, verifying readiness,
   switching ingress/load-balancer routing, and retiring old runtime replicas
   are orchestrator responsibilities. Use the internal orchestrator
   deployment/environment-revision contracts for container app implementation
   work. The Control Plane now has an internal deployment-apply boundary for
   dispatching a deployment spec to the selected orchestrator, and running
   image deployments use that boundary when runtime reconciliation is required.
   Defer full rollout history, restore UI, app revision management, traffic
   splitting, readiness-gated cutover, and advanced rollout controls to later
   container app deployment slices. Deployment-applied replicas now have
   app-revision-scoped runtime identity and local superseded-replica retirement
   as the foundation for the side-by-side replacement path.
7. Keep container app replica diagnostics app-scoped in Scale and replicas. It
   shows app-owned replica/runtime diagnostics to users who can view or manage
   the container app without requiring the global runtime-managed inventory
   view; the global `Show runtime-managed resources` setting remains for
   browsing hidden runtime-managed artifacts directly in the resource
   inventory.
8. Add an app-scoped Monitoring tab for container apps that summarizes
   provider-observed resource metrics for the app and shows each materialized
   runtime replica/container separately. This should use the resource
   Monitoring menu item under Management, not the shared Telemetry metrics
   surface, because CPU, memory, network, block I/O, process count, restart,
   uptime, and provider materialization state are resource metrics.
9. Validate the managed-service story with samples that combine container app,
   storage, service discovery, identity, secrets/configuration, logs, traces,
   and public/name exposure.

## Remaining Tasks

* Materialize container app volume mounts reliably through the supported local
  runtime paths. Resource Manager volume assignment and Scale and replicas now
  honor provider-projected volume access modes when warning about replica
  fan-out: write mounts require `ReadWriteMany`, while read-only mounts can use
  `ReadOnlyMany` or `ReadWriteMany`.
* Add application-centric UI for internal exposure and public endpoint
  exposure. Load-balancer and DNS/domain mapping authoring now have first
  app-centric entry points, and Resource Manager can add or edit routes on an
  existing load balancer. Richer provider-specific publishing and custom domain
  guidance remain open.
* Continue conflict and readiness diagnostics before start/update where
  possible. Load-balancer route-shape validation, duplicate route-match
  validation, duplicate entrypoint validation, host-port conflict validation,
  missing target/endpoint action-capability reasons, and pending-publish
  diagnostics for provider-selected name mappings exist; remaining work should
  focus on DNS/name provider-runtime diagnostics where they still affect MVP
  flows.
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
* Add telemetry scope dimensions to container app logs, traces, and telemetry
  metrics so Resource Manager can offer an `All instances` default plus
  per-instance scope filtering when a replicated app has multiple runtime
  instances. Single-instance apps should not show a selector.
* Keep local container-registry configuration explicit so CloudShell does not
  assume `localhost:5000`. The Container App Deployment sample already uses an
  explicit non-default port, and declared Docker container resources now
  preflight occupied local TCP/HTTP endpoint ports before Start. Future
  registry-backed providers should also suggest or allocate alternate ports
  when a default is unavailable.
* Continue evolving the container app deployment operation as the first
  Resource Manager orchestration deployment path. The operation should validate
  the generalized deployment model, not make deployment semantics
  container-app-specific. It can include requested replica count and records
  app-owned deployment/revision history. The orchestrator now materializes
  deployment-applied replicas with app-revision-scoped identity, records
  explicit routing update milestones, rolls back the candidate replica group on
  setup failure, and tears down superseded runtime replica groups as a separate
  post-apply operation. Failed apply now marks the candidate app
  deployment/revision failed and keeps the previously active app revision
  active. Restore is a container app configuration operation: a later restore
  should create a new app deployment based on the configuration captured by a
  selected app revision, producing a new app revision that records the based-on
  app revision relationship rather than reactivating the selected revision
  object.
  Deployment materialization now waits for declared HTTP startup/readiness
  checks, or HTTP health checks when no explicit startup/readiness check is
  present. The Resource Manager UI now keeps Deployment focused on deploying
  an image and reading deployment events, while Revisions separately shows the
  current and previous app configuration revisions. Post-apply cleanup failures
  are warnings instead of failures of the already-applied app revision. Remaining
  rollout work should retain failed runtime/app revision diagnostics and make
  traffic and cleanup policy configurable.
* Continue improving update behavior around replica, environment, endpoint,
  identity, and storage changes, deciding which changes belong to active
  revision capacity/configuration and which require a new deployment revision.
* Keep graph-backed container apps compatible with existing provider-specific
  UI by projecting the effective replica-mode facts that views use for
  deployment, monitoring, and scale decisions. The graph should keep
  `container.replicas` as the declarative configuration; Resource Manager
  projection can derive `container.replicas.enabled` and requested replica
  slots for existing runtime/UI consumers.
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
* What is the smallest useful app configuration revision history that belongs
  to the container app before the richer orchestrator environment-history model
  lands?
* What readiness signal is sufficient for switching a local container app to a
  newly deployed revision when no explicit health checks are declared?
* Which telemetry scope dimension names should become stable contract
  fields versus provider-owned attributes on logs, spans, and metric points?
