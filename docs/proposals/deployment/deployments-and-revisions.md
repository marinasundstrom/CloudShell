# Deployments and Revisions Proposal

> This proposal depends on the [Runtime managed resource](runtime-managed-resource.md) proposal.

## Status

In progress.

CloudShell already has a Resource Manager, a resource graph, an orchestrator
abstraction, a default orchestrator, a Docker Compose-based orchestrator, and
an orchestrator-level service abstraction.

The current orchestration model can manage standalone runtime resources and can
group runtime instances through services, but there is no formal deployment and
revision model for recording requested workload changes, versioned workload
state, rollout history, replica changes, or traceability across orchestrator
implementations.
Standalone resource management remains the default mode: the resource itself is
the orchestrated unit, and declared dependency relationships are still managed
by the orchestrator when actions require dependency ordering or dependency
startup. Services, deployments, revisions, and replica groups extend the
orchestrator model for systems that scale; they should not make every
orchestrated resource pretend to be a scaled service.

Container app image updates are the clearest motivating case. Updating the
image for a running container app should not be modeled as "restart this
resource." It should deploy a new revision for the requested image, optionally
with a new requested replica count. That revision starts its containers next to
the currently serving revision. Once the new revision is ready, CloudShell can
switch traffic or endpoint routing to it and then retire the old revision.
Scaling the currently active revision is a separate operation that adjusts
desired capacity without necessarily creating another revision.

Initial implementation now adds internal data contracts for
`ResourceOrchestratorDeployment`, `ResourceOrchestratorDeploymentSpec`, and
`ResourceOrchestratorRevision` in the orchestration abstractions, plus an
opt-in `IResourceOrchestratorDeploymentApplier` boundary and Control Plane
dispatcher for applying a deployment through the selected orchestrator.
These are CloudShell runtime concepts: the orchestrator manages resources and
their runtime configuration, and deployment/revisioning records the desired and
materialized CloudShell runtime state rather than exposing a Kubernetes,
Docker Compose, or other provider-native deployment object as the domain model.
The orchestrator is part of Resource Manager's execution layer. Resource
Manager is the umbrella concept for the services that manage resources:
lifecycle, graph validation, authorization, grouping, persistence, and runtime
materialization. Its public surface is the logical facade for resource-facing
operations, while orchestrators are how Resource Manager materializes runtime
services and resources behind that facade.
Providers can opt into `IResourceOrchestratorDeploymentProvider` to describe
the deployment spec that should be applied after a domain update. These are
intended for container apps, providers, and orchestrators to build on first.
A deployment apply is incremental setup: it creates or updates the specified
runtime resources by stable id. It does not implicitly remove omitted resources.
Resource Manager orchestration now has an internal service tear-down boundary
for stopping or deleting the runtime resources that belong to an orchestration
service. Tear-down is a separate operation over individual runtime resources,
replica groups, or all resources belonging to an orchestration service.
Container apps now use the deployment contract to project deployment status,
service id, workload version, requested replicas, and materialized replica count
onto the stable app resource and Deployment tab. Materialized runtime replica
resources also carry the deployment id, service id, and deployment revision
they implement for traceability. The deployment represents the requested
runtime state. The revision represents the materialized result of that
deployment: the current state when active and the past state when retained for
history, diagnostics, or restore-to-state workflows. Restoring means creating a
new deployment whose requested state is based on the materialized state
captured by a selected prior revision; it does not reactivate or mutate that
revision object. If the deployment succeeds, the resulting revision records the
based-on revision relationship for change tracking. Container app image
deployment now records
the app revision through the app provider and, when runtime reconciliation is
required, asks the Control Plane to apply the provider-described orchestrator
deployment spec. The default orchestrator now records deployment activity
events for service reconciliation and replica materialization so Resource
Manager activity can trace what the orchestrator is doing during apply. It
also records routing update milestones after endpoint-bearing replicas are
materialized, making the intended cutover from previous runtime replicas to
the newly materialized revision visible without exposing traffic-splitting
controls yet.
In this model, the container app resource defines and manages the
resource-owned deployment it wants Resource Manager to apply. That deployment
targets an identifiable orchestrator service tracked by the container app. The
orchestrator service unit is the runtime boundary that can contain service
routing or loader materialization plus a replica group with the requested
number of replicas for a specific image and replicated configuration.
The Control Plane also records internal orchestrator deployment history for
apply attempts, successful orchestrator revisions, and failed apply results.
When the default orchestrator applies a deployment, it now materializes service
instances with revision-scoped runtime names so new replicas can be started next
to the currently serving revision before routing is remapped. Container app
replica materialization now waits for declared HTTP startup, readiness, or
health checks before a replica is reported as materialized and before the
orchestrator revision is produced. Superseded runtime instances are described
as explicit replica-group tear-down targets after deployment setup, keeping
cleanup separate from deployment apply.
Container apps also keep provider-owned app deployment and revision history
separately from the desired application definition. Those app deployment
records correlate the deployment request to the produced container app
revision and to the orchestrator deployment used to materialize runtime state,
but they are not the same entity as orchestrator deployments or orchestrator
revisions.
When orchestrator apply fails after a resource-owned revision has been
recorded, the provider is notified so it can mark the candidate resource
deployment and revision as failed and keep the previously active resource
revision active. For container apps this prevents an unmaterialized app
revision from being presented as the active successful revision while still
retaining failed-attempt history.
The intended general rule is broader than container apps: when an orchestrator
handles a resource state change that has runtime workload intent, it may derive
a default deployment for that change even when the user manages the resource
directly rather than managing an explicit deployment resource. That implicit
deployment gives standalone resource changes the same traceability as service
deployments: the current materialized state of a resource can be associated
with the deployment, implicit or explicit, that produced the active revision.
They are not yet a public Resource Manager or Control Plane management surface.
Public APIs for orchestrator-level deployments may be useful later, but they
are not a current use case. Rich rollout history, restore deployments, traffic
splitting, retention, and live resource/orchestrator graph visualization remain
deferred.

## Problem

CloudShell resources may express desired runtime behavior that requires orchestration.

Examples include:

* container app replicas
* runtime containers
* replica groups
* scaled workloads
* rolling updates
* image changes
* environment changes
* configuration changes
* endpoint changes
* runtime-managed sub-resources

Today, these changes can be applied by orchestrators, but CloudShell lacks a unified deployment primitive that represents:

* what was applied
* when it was applied
* which resource caused it
* which service it affected
* which replicas were created
* which runtime resources belong to the applied version
* what revision was produced by the change
* how traffic or endpoint routing moved from the previous revision to the new
  revision

Without deployments and revisions, several problems appear:

* replica creation becomes orchestrator-specific
* rollout history becomes fragmented
* restore from previous state is harder to model
* traceability is incomplete
* resource changes cannot easily be correlated with runtime changes
* default and Docker Compose orchestrators may represent changes differently
* higher-level resources must know too much about runtime orchestration
* runtime-managed sub-resources lack a versioned parent context

CloudShell needs a unified way to represent versioned orchestration changes while keeping the Resource Manager as the authority over the resource graph.

## Goals

* Introduce Deployment as CloudShell's orchestrator-level representation of
  desired runtime state.
* Introduce Orchestrator Revision as the materialized runtime outcome produced
  by applying an orchestrator deployment.
* Allow an orchestrator to derive a default deployment for a resource state
  change when the resource has runtime workload intent.
* Allow higher-level resources to request orchestration without managing replicas directly.
* Allow orchestrators to compute runtime resources and service replicas from resource intent.
* Support consistent deployment behavior across the default orchestrator, custom
  orchestrators, Docker Compose integrations, Kubernetes integrations, and
  future orchestrator adapters.
* Support traceability from user-managed resources to deployments, revisions, services, replicas, and runtime-managed resources.
* Enable orchestration-level restore, diagnostics, and history inspection while allowing higher-level resources to maintain their own domain-specific revision models.
* Keep deployments and revisions in the orchestrator layer rather than making them normal user-authored resources.
* Allow deployments and revisions to participate in diagnostics and runtime inspection.
* Preserve the existing Resource Manager responsibility for validation, lifecycle coordination, state, ownership, and graph management.

## Non-Goals

* Do not replace the existing orchestrator abstraction.
* Do not replace the existing orchestrator-level service abstraction.
* Do not copy Kubernetes, Docker Compose, or any other orchestrator's object
  model as CloudShell's domain model.
* Do not make Deployment a normal user-authored resource.
* Do not require every resource to use deployments.
* Do not require users to explicitly create or manage deployment resources for
  ordinary resource lifecycle and configuration changes.
* Do not require every orchestrator to implement advanced rollout strategies immediately.
* Do not expose Docker Compose, Kubernetes, or container-host-specific implementation details in the common deployment model.
* Do not move resource graph ownership from the Resource Manager to the orchestrator.
* Do not require resources to manually create their own replicas.
* Do not make deployments responsible for general resource dependency resolution.

## MVP Definition

The MVP is an internal Resource Manager orchestration and deployment capability.
It generalizes how Resource Manager asks an orchestrator to materialize desired
runtime state, track the resulting runtime outcome, and tear down runtime units
that no longer belong to the active state. Container apps are the first
validation path because they combine image deployment, explicit replicas,
service exposure, health, logs, and runtime-managed replica resources. They
should prove the abstraction, not own it.

The same model should remain available to other workload resources, such as
ASP.NET Core project containerization, Docker Compose-backed services, imported
services, or custom providers that need Resource Manager to materialize a
desired runtime state. The MVP should prove the runtime model without
committing CloudShell to public deployment-management APIs or to one
orchestrator's native object model. The default orchestrator is the first
implementation, and future orchestrators should implement the same Resource
Manager contracts rather than redefining deployment semantics.

The MVP must support:

* A resource-owned deployment request that describes workload intent. The first
  implementation is container app image deployment, optionally with requested
  replicas.
* Resource-owned deployment and revision history, separate from the desired
  resource definition. The first implementation is container app-owned
  deployment and revision history.
* A provider-described orchestrator deployment that says "this is the runtime
  state I want" for the selected Resource Manager orchestrator.
* A Resource Manager deployment service that records apply attempts, selects a
  deployment applier, and produces revision outcomes separately from the
  broader orchestration service.
* A default deployment applier that incrementally sets up the requested
  orchestrator service and revision-scoped replica group for orchestrators that
  do not have their own native deployment concept.
* An orchestrator revision outcome only after apply succeeds, including the
  materialized replica group snapshot.
* Failed deployment attempts recorded without producing an orchestrator
  revision.
* Provider-owned failure handling when resource revision history was recorded
  before orchestrator apply, so the candidate resource revision can be marked
  failed and the previously active resource revision can remain active.
* A first readiness gate before revision activation. Container apps use
  declared HTTP startup/readiness checks when present, otherwise declared HTTP
  health checks, without introducing advanced traffic policy machinery.
* Deployment activity events for apply, service reconciliation, replica
  materialization, rollback, routing milestones, post-apply cleanup, success,
  and failure.
* Best-effort rollback of the candidate deployment unit when setup fails before
  a revision is produced.
* Explicit post-apply replica-group tear-down for superseded runtime replicas,
  separate from deployment setup. Cleanup failures are warning diagnostics on
  the applied deployment rather than failures of the already-produced active
  revision.
* Active-revision replica scaling through the replica-group change model
  without forcing a resource restart or creating a new workload revision.
* Projection of active deployment, active resource revision, requested replicas,
  materialized replica count, deployment service id, deployment revision id,
  and replica-group id onto the stable resource and runtime-managed resources.
  The first projection target is the stable container app and its hidden runtime
  replica resources.
  Requested replicas are the count requested by the resource/provider; the
  orchestrator may materialize fewer replicas when capacity, placement, policy,
  or runtime failures prevent granting the full request.
* Focused tests for successful apply, failed apply, rollback logging, image
  deployment with post-apply tear-down, cleanup warning behavior, active
  revision scaling, and concurrent deployments for different container apps.

The MVP should add or refine next:

* Keep rollback scoped to the candidate deployment unit. Restore is a future
  user or rollout-policy change operation based on the state captured by a
  selected revision; it is not MVP rollback and it does not mutate or
  reactivate the older revision.
* Keep configurable cleanup and retention policy deferred until the basic
  diagnostics and runtime cleanup model has proven useful.

The MVP deliberately leaves these flexible:

* Whether orchestrator deployments and revisions become a public Control Plane
  API.
* Whether revisions are projected as runtime-managed resources, stored only as
  orchestrator metadata, or both.
* The exact long-term readiness model, traffic shifting model, drain policy,
  retention policy, restore workflow, and revision-lineage projection.
* Which resource types opt into the deployment model next and which continue as
  standalone orchestrated resources.
* How Docker Compose, Kubernetes, or custom orchestrator adapters map the
  common contracts to their native objects.
* Live graph visualization of Resource Manager and orchestrator activity.

## Domain Model

### Revision Terminology

The term `Revision` is used in different CloudShell subdomains and should not
be treated as a single global concept.

A resource type may define its own revision concept as part of its domain model.
For example, a Container App may have Container App deployments that produce
Container App revisions. Those revisions record materialized application
state, including traffic behavior, image references, environment settings, and
state that can be used as input to a later restore deployment.

The orchestrator may also define revisions. An orchestrator revision represents
a versioned runtime realization of a deployment. It tracks the workload shape
that was applied, the service state that resulted, and the runtime-managed
resources associated with that version.

These concepts may be correlated, and they may even share identifiers for
traceability, but they are not the same entity.

```text
Container App Deployment
        ↓
produces
        ↓
Container App Revision
        ↓
materialized by
        ↓
Orchestrator Deployment
        ↓
Orchestrator Revision
        ↓
Service + Runtime Resources
```

Each subdomain owns the revision data relevant to its own behavior.

Container App revisions answer questions such as:

* what application state was materialized?
* which image and environment settings are part of this app version?
* which revision receives traffic?
* which revision captures state that can be used for a restore deployment?
* which deployment produced this revision?
* which revision state this revision was based on?

Orchestrator revisions answer questions such as:

* what workload definition was applied?
* which orchestrator service was reconciled?
* which replicas were set up or torn down?
* which runtime-managed resources belong to this applied deployment?
* what rollout state resulted?

The deployment and revision model in this proposal refers specifically to
orchestrator deployments and orchestrator revisions unless otherwise stated.

A Deployment represents the desired runtime state as specified by the actor
deploying the workload. It describes the workload shape, service grouping,
replica request, image or workload version, ports, dependencies, and other
runtime intent that should be materialized. In other words, deployment is where
the requested change is stated. Applying a deployment is a setup operation:
specified resources are created or updated by id, and omitted resources are
left alone unless a separate scale-down, revision-retirement, or service
tear-down operation targets them. A deployment may be explicit in a future
management surface, but the MVP path treats it primarily as a default
deployment derived by the orchestrator when a resource state change needs
runtime materialization. A deployment may also be initialized from the
materialized state captured by a retained revision. That is the restore or
revert-like model: the revision supplies state for a new deployment, but the
revision itself is not restored in place.

An Orchestrator Revision represents the outcome: the materialized runtime state
produced when an orchestrator applies a deployment. It records the resulting
applied version and the runtime-managed resources or provider-native artifacts
associated with that application of desired state, allowing CloudShell to
version and track runtime-environment changes and eventually restore to the
state captured by older revisions. A revision is the snapshot result of the
deployment, not the individual change itself. It should be immutable once
recorded; a restore creates another deployment and another revision, with a
based-on revision relationship back to the revision that captured the requested
state.

This is a CloudShell abstraction, not an attempt to copy Kubernetes
Deployments, Docker Compose services, or another orchestrator's native model. A
custom orchestrator can execute the contract directly. An integration
orchestrator can translate the same contract into provider-native objects, such
as Docker Compose services or Kubernetes workload and service resources, while
keeping those provider details outside CloudShell's common domain model.

Suggested model:

```csharp
public sealed class OrchestratorDeployment
{
    public string Id { get; init; }
    public string OrchestratorId { get; init; }
    public string SourceResourceId { get; init; }
    public string? BasedOnRevisionId { get; init; }
    public string ServiceId { get; init; }
    public string RevisionId { get; init; }
    public DeploymentSpec Spec { get; init; }
    public DeploymentStatus Status { get; init; }
}
```

```csharp
public sealed class OrchestratorRevision
{
    public string Id { get; init; }
    public string DeploymentId { get; init; }
    public string SourceResourceId { get; init; }
    public string? BasedOnRevisionId { get; init; }
    public string ServiceId { get; init; }
    public int RevisionNumber { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public RevisionStatus Status { get; init; }
}
```

The deployment model should represent the orchestrator’s computed workload intent, not the full user-facing resource graph.

For change tracking, the restore relationship should be represented as:

```text
Deployment <deployment-id>
  Based on revision <base-revision-id>

Revision <new-revision-id>
  From deployment <deployment-id>
  Based on revision <base-revision-id>
```

The deployment's `BasedOnRevisionId` records which revision state was used as
input. The resulting revision's `DeploymentId` records the change that produced
it, and its `BasedOnRevisionId` records the prior revision state it was based
on. For ordinary deployments, `BasedOnRevisionId` defaults to the current
active or latest successful revision for that resource or service. A restore
deployment explicitly selects an older revision as its base. This keeps the
normal change history linear by default while still allowing explicit branches
for restore or future merge workflows.

When a deployment is based on a revision, its default requested state is the
state captured by that revision. The actor may still include additional
resources, overrides, or other deployment input before apply. In that case the
deployment is still based on the selected revision, but the resulting revision
records the newly materialized state produced by the full deployment request.

This should be implemented at the orchestrator level as metadata and state
tracking, not as a product-specific restore command. The selected orchestrator
should accept and persist `BasedOnRevisionId`, default it to the active or
latest successful revision when the caller does not specify a base, retain the
materialized state needed to author a later deployment from a revision, and
return the resulting revision with both `DeploymentId` and
`BasedOnRevisionId`. Resource Manager, deployment services, or provider-owned
flows decide when to author a restore, revert-like, or merge deployment and
which additional deployment input is applied before materialization.

The same change-tracking model should eventually allow state merge from
revisions. Merging does not mutate the selected revisions and does not bypass
deployment. It is an authoring operation that takes one or more retained
revision state snapshots, optionally consults the deployments or change records
that produced those revisions for diff context, resolves the selected state
against a chosen base, and produces a final deployment representing the desired
state that could be applied. If that deployment succeeds, the new revision
records the deployment that produced it and the revision relationships used to
build its requested state.

### Resource and Orchestrator Boundary

Higher-level resource infrastructure owns the domain request and the
domain-specific history. Orchestrators own runtime rollout mechanics.

For container apps, the container app infrastructure owns:

* accepting a container app deployment request
* validating app-level deployment input such as image, app configuration,
  requested replicas, ingress intent, and identity references
* producing and tracking container app revisions
* recording based-on revision, trigger, requested replica count, and revision
  status in the container app domain model
* recording app deployment and revision history in provider-owned operational
  state rather than embedding unbounded rollout history in the desired app
  definition
* asking the selected orchestrator to materialize the requested app revision

The orchestrator owns the mechanics required to apply that request:

* computing or updating the orchestrator deployment
* producing the orchestrator revision or equivalent runtime version metadata
* creating new runtime replicas next to the currently serving replicas when
  the rollout strategy requires availability
* waiting for readiness or health gates
* remapping service routing, ingress, backend pools, or load-balancer targets
  to the new runtime replicas
* draining, stopping, or deleting old runtime replicas after cutover
* reporting rollout state, failure state, and runtime-managed resource
  ownership back to the Resource Manager projection

These responsibilities are not unique to container apps. Any resource type
that expresses runtime workload intent should be able to request orchestration
without knowing how a Docker Compose service, Kubernetes Service, Traefik
route, load balancer, local container group, or future orchestrator performs
the rollout.

The boundary is intentionally asymmetric: the resource domain can decide what
changed and which app revision should exist, but it should not directly
manipulate orchestrator-owned replicas, routing tables, backend registrations,
or cleanup behavior.

Deployment application is scoped to a resource and deployment. CloudShell must
be able to apply deployments for different container apps concurrently; any
serialization should be limited to the same resource, same runtime target, or
provider-specific critical section that truly cannot be run in parallel.
The deployment model should retain enough status, timing, and revision
correlation for diagnostics. A future live graph of Resource Manager and
orchestrator activity should build on broader event observation rather than
being tied only to orchestrator deployment records. That visualization could
show several container app deployments progressing at the same time: the
orchestrator materializing runtime resources, updating existing runtime
resources, acting on routing or ingress resources, and cleaning up superseded
runtime resources.

## Conceptual Flow

```text
User-managed Resource
        ↓
Resource Manager validates graph and lifecycle
        ↓
Resource Manager orchestration receives desired runtime state
        ↓
Default Deployment is created or updated
        ↓
Selected orchestrator sets up the deployment
        ↓
Revision is produced as the outcome
        ↓
Service and replicas are reconciled
        ↓
Runtime-managed resources named by the deployment are created or updated
```

Example:

```text
ContainerApp
 ├── image: myapp:v2
 ├── replicas: 3
 └── port: 8080
```

may produce:

```text
Deployment: api
 └── Revision: api-r12
      └── Service: api
           ├── Replica: api-r12-1
           ├── Replica: api-r12-2
           └── Replica: api-r12-3
```

## Deployment Model

A Deployment represents desired runtime state for an orchestrated service. In
the common path, a stable resource remains individually manageable by Resource
Manager, while the orchestrator generates a default deployment for each
deployment-relevant state or configuration change. This lets CloudShell track
what was requested without requiring the user to author a deployment resource.

It may include:

* source resource reference
* service reference
* requested replica count
* runtime template
* image reference
* environment configuration
* ports
* mounts
* identity references
* endpoint requirements
* rollout strategy
* traffic or endpoint cutover policy
* revision policy
* ownership metadata

A Deployment should answer:

* what is being deployed?
* which resource requested it?
* which service does it affect?
* what desired runtime shape should be materialized?
* what revision was created?
* what runtime resources were produced?

## Revision Model

A Revision represents a specific materialized outcome of a deployment.

A new revision should be created when deployment-relevant inputs change.

Examples:

* image changed
* environment changed
* command changed
* port changed
* mount changed
* identity changed
* scale template changed
* endpoint configuration changed
* resource template changed

Replica count changes need more nuance. A manual scale operation against the
currently active revision can reconcile the service replica count while
preserving that revision association. A deployment operation can also include
a requested replica count for the new revision, in which case the new revision
should be materialized at that target capacity before cutover when the rollout
strategy requires availability.

A revision should allow CloudShell to determine:

* which runtime resources belong to a version
* which version is currently active
* which version was previously active
* which version failed
* which version can provide state for a state-restore deployment
* which prior revision or deployment the revision was based on
* which resource change caused the revision

### Restore To Revision State

Restoring means restoring to the materialized state captured by an existing
revision. It does not restore the revision object. CloudShell models this as a
new deployment whose requested state is based on the selected prior revision's
state snapshot instead of the default latest revision. If the deployment
succeeds, CloudShell records a new revision as the outcome and links that
revision back to the based-on revision used to define the requested state.

This makes restore part of change tracking. Revisions record materialized
state snapshots. Deployments record requested changes to state. A
state-restore deployment is a revert-like change record that says "make the
current runtime state look like the state captured by revision r12." By
default it recreates that captured state, but the deployment may also specify
additional resources, overrides, or other desired-state input before execution.
This keeps revisions immutable while still supporting recovery from a bad
change, such as an invalid image, network configuration, resource reference, or
replicated resource configuration. A typical history may look like:

```text
Revision r12: known-good image and network config
    |
Deployment d13: image update with bad config
    |
Revision r13: failed or unhealthy materialized state
    |
Deployment d14: restore to state captured by revision r12
    |
Revision r14: new active state, basedOnRevisionId = r12
```

The state-restore deployment may use the selected revision state exactly, or it
may start from that state and include additional deployment input before
execution. The resulting revision should retain enough lineage to answer "what
state was this revision based on?" and "which change did it replace?" Over time
this can become a revision lineage graph or tree rather than only a flat list.

Rollback is different. MVP rollback is best-effort cleanup of the candidate
deployment unit when setup fails before activation. Restore is an explicit
change operation after a previous deployment produced an
undesired, failed, or unhealthy state.

### Merging State From Revisions

Revision lineage can also support merge workflows. A user may want the network
configuration from one revision, the image or environment state from another,
and the replica configuration from the current active state. CloudShell should
model that as a deployment-authoring step: choose a base revision state, merge
selected state fragments captured by retained revisions, resolve conflicts,
then produce a deployment that represents the final desired state.

The deployment is the object applied by the orchestrator. The selected
revisions are materialized-state inputs to the authoring process; they are not
the changes themselves. A successful apply produces a new revision that records
the deployment it came from and the based-on revision relationships needed to
explain how the desired state was assembled.

This is future revision-management behavior, not part of the MVP rollout path.
The MVP only needs to retain enough state, deployment diff context, and lineage
information that a future merge UI or API could construct a deployable state
without treating revisions as mutable resources or individual change records.

## Service Relationship

CloudShell already has an orchestrator-level Service abstraction.

Deployments and revisions should build on this abstraction.

A Service is the logical runtime grouping used by the orchestrator.

A Deployment defines the desired versioned runtime state for that Service.

A Revision represents the concrete materialized outcome of executing that
Deployment.

Within a service, each materialized revision should have an orchestrator-owned
replica grouping. This grouping is not a Kubernetes ReplicaSet and should not be
introduced as a normal user-authored Resource Manager resource. The structure
can look similar to Kubernetes because it needs to group runtime instances by
revision, but CloudShell should reuse resource instances as the unit rather
than inventing a pod concept. The replica group is the default way the
orchestrator handles service replication. Resource providers and adjacent
resources such as load balancers should consume the orchestrator-managed group
when materializing runtime resources, while still being able to manipulate the
group and its members individually when the target runtime has no higher-level
primitive. It is the runtime boundary that lets the orchestrator track which
replica resources belong to one service revision, compare requested and
materialized replica count, observe readiness, remap routing from one
revision's replicas to another, and retire superseded replicas after cutover.

The service, deployment, and revision concepts are intentionally provider
neutral. The default local orchestrator maps them to convention-named
containers and ingress configuration. A Docker Compose orchestrator can map
them to Compose services and generated configuration. A Kubernetes-oriented
orchestrator can map them to Kubernetes workload and service resources. In each
case CloudShell's model remains the same: desired state enters through a
deployment, and the orchestrator reports the resulting revision.

Example:

```text
Service: api
 ├── Active Revision: api-r12
 │    └── Replica Group
 │         ├── api-r12-1
 │         ├── api-r12-2
 │         └── api-r12-3
 ├── Previous Revision: api-r11
 │    └── Replica Group
 │         ├── api-r11-1
 │         ├── api-r11-2
 │         └── api-r11-3
 └── Routing: active -> api-r12
```

The Service remains the stable runtime grouping primitive. The Replica Group is
the revision-scoped runtime set inside that service.

Deployment and Revision add versioning, rollout history, and traceability.
During deployment application, the requested runtime state should describe the
stable service, the service routing or load-balancer configuration, and the
replica group required for that revision. A deployment of image X with N
replicas should ask the orchestrator to materialize that runtime state, not ask
the container app provider to manually own N runtime containers. That lets an
orchestrator start a replacement replica group for a new image revision next to
the currently serving group, update service routing or ingress to the new
group, and then run a separate replica-group tear-down operation to drain or
remove the superseded group. Ordinary active-revision scaling can reconcile the
existing replica group by adding members during setup or tearing members down
when the requested capacity decreases because it is capacity management for the
current revision rather than a workload replacement.

The current revision-scoped replica names are only the first materialization of
this grouping. The orchestrator revision outcome should retain the materialized
replica group snapshot so runtime replica resources can be associated with
their deployment service and revision across materialization, readiness,
cutover, diagnostics, and cleanup.

Replica-group tear-down is an orchestrator-provider boundary. A provider can
describe which superseded replica group should be retired after deployment
setup, and Resource Manager orchestration asks the selected orchestrator to tear
that group down separately from the deployment apply operation.

## Resource Relationship

User-managed resources should not directly manage replica creation.

A resource should express desired runtime behavior.

Example:

```text
ContainerApp
 ├── Image
 ├── Environment
 ├── Ports
 ├── Replicas
 ├── Dependencies
 └── Runtime requirements
```

The Resource Manager validates the resource and its graph relationships.

The orchestrator computes the deployment, revision, service state, and runtime-managed sub-resources.

This keeps resource definitions focused on intent rather than runtime implementation details.

For example, a container app deployment may request image `api:v2` with three
replicas. The container app records the app revision and asks the orchestrator
to apply it. The orchestrator decides how to create the new runtime replicas,
verify readiness, move the ingress or load-balancer mapping, and clean up the
old runtime replicas.

## Orchestrator Responsibilities

Orchestrators are responsible for:

* creating deployments
* producing revisions
* reconciling services
* creating and removing replicas
* applying rollout behavior
* applying routing or load-balancer cutover
* draining and cleaning up superseded runtime replicas
* tracking active and previous revisions
* defaulting deployment `BasedOnRevisionId` to the active or latest successful
  revision when no explicit base is supplied
* retaining the materialized revision state needed to author a later deployment
  based on that revision
* returning revision outcomes with `DeploymentId` and `BasedOnRevisionId` for
  traceability
* reporting deployment status
* reporting revision status
* mapping the common deployment model to backend-specific behavior

Orchestrators should not own the product workflow named restore or merge. They
own deployment application, revision outcome recording, based-on revision
metadata, and enough materialized state for higher-level Resource Manager or
provider workflows to author a future deployment from prior revision state.

The default orchestrator may manage services and replicas directly using the default container host.

The Docker Compose orchestrator may project deployments into Docker Compose services, containers, and scale operations.

Future orchestrators may project deployments into Kubernetes Deployments, ReplicaSets, Pods, or other platform constructs.

## Resource Manager Responsibilities

The Resource Manager remains responsible for:

* resource graph ownership
* resource identity
* dependency resolution
* lifecycle coordination
* validation
* authorization
* state tracking
* ownership relationships
* diagnostics projection
* dispatching lifecycle operations to orchestrators

The Resource Manager decides whether a resource can start, stop, update, or reconcile.

The orchestrator performs the runtime computation and materialization.

## Default Orchestrator Behavior

The default orchestrator should treat Deployment and Revision as native orchestration concepts.

Example behavior:

```text
Deployment created
Revision created
Service reconciled
Replica resources created
Containers started through default container host
Revision marked active
```

For scale changes:

```text
Deployment updated
Revision may or may not change depending on policy
Service reconciled
Replica count adjusted
```

For image or template changes:

```text
Deployment updated
New revision created
New revision replicas created to the desired count next to current replicas
Startup/readiness checks pass for the new replicas
Traffic or endpoint routing switches to the new revision
Old replicas drained or stopped after cutover
Revision marked active
Previous revision retained for history
```

## Rollout and Availability Semantics

Deployment-relevant updates to a running service should be revision-based.
They should not mutate or restart the active revision in place. The old
revision remains the serving version until the new revision is ready, unless
the user explicitly chooses a disruptive strategy.

The baseline rollout strategy for a container app should be:

1. Deploy a new revision from the requested image, environment, endpoint,
   storage, identity, and requested replica settings.
2. Materialize the new revision's runtime resources without deactivating the
   current serving revision.
3. Wait for the new revision to provision, reach the desired minimum serving
   capacity, and pass startup/readiness or liveness gates that are relevant to
   routing.
4. Switch traffic, ingress configuration, or endpoint routing to the new
   revision.
5. Drain or stop the old revision according to the rollout and retention
   policy.

This model allows image updates to be combined with a requested replica count.
For example, deploying `api:v2` with three replicas should create a new
revision at three replicas, verify that enough replicas are ready, then switch
serving traffic from the old revision to the new revision. If the new revision
fails readiness, the old revision remains active and the failed revision stays
inspectable for diagnostics.

Deployment apply fails when the orchestrator cannot set up the requested
runtime state: for example, the spec is invalid for the selected orchestrator,
provider materialization throws, a runtime resource cannot be created or
updated, routing cannot be updated, or an explicit deployment readiness gate
fails. A workload resource that starts and later reports unhealthy liveness is
runtime health, not automatically an orchestrator deployment failure, unless
that health or readiness check was declared as part of the deployment gate.
When setup fails before a materialized outcome is accepted, Resource Manager
records a failed deployment attempt and no orchestrator revision is produced.
The selected orchestrator should then make a best-effort rollback of the
deployment unit it was setting up, usually by tearing down the candidate replica
group. Rollback failure must be logged but should not hide the original apply
failure. Automatic restore to the previously serving state is a separate
rollout policy, not the baseline meaning of deployment apply failure.

Advanced strategies such as blue/green, canary percentages, labels, gradual
traffic shifting, and automatic restore-to-state can build on the same
deployment and revision model. They are not required for the first
implementation, but the baseline model should not force a full resource restart
for image updates.

Scale-only operations remain different. Increasing or decreasing replicas for
the active revision reconciles runtime capacity and should not require traffic
cutover or replacement of healthy existing replicas unless an orchestrator's
backend requires it. The replica group model should own the change calculation:
scale-up sets up target group members, scale-down tears down previous group
members, and unchanged members remain part of the active runtime set.

## Docker Compose Orchestrator Behavior

The Docker Compose orchestrator should map the common deployment model to Docker Compose constructs.

Example mapping:

| CloudShell Concept | Docker Compose Concept             |
| ------------------ | ---------------------------------- |
| Deployment         | Compose project/service definition |
| Service            | Compose service                    |
| Replica            | Compose-managed container          |
| Revision           | CloudShell-managed metadata        |
| Scale              | Compose scale behavior             |

Docker Compose does not provide the same revision model as Kubernetes, so CloudShell should maintain revision metadata itself.

## Runtime-Managed Resource Relationship

Deployments and revisions may create runtime-managed resources.

Examples:

* replicas
* containers
* container images
* volume attachments
* endpoint registrations
* service registrations
* health probes

These runtime-managed resources should be associated with:

* the source resource
* the deployment
* the revision
* the orchestrator service

Example:

```text
ContainerApp
 └── Deployment api
      └── Revision api-r12
           └── Service api
                ├── Replica api-1
                │    └── ContainerInstance
                ├── Replica api-2
                │    └── ContainerInstance
                └── Replica api-3
                     └── ContainerInstance
```

This provides a clean ownership and diagnostic path from user intent to runtime state.

## Lifecycle Management

Deployments and revisions should participate in orchestration lifecycle operations.

### Initial Deployment

```text
Resource created
Deployment created
Revision created
Service created
Replicas created
Runtime resources started
```

### Deploy Revision

```text
Revision-relevant resource intent changed
Deployment updated with new revision spec
New revision created
New revision replicas started next to active revision replicas
New revision readiness verified
Traffic or endpoint routing switched to new revision
Previous revision retained, drained, or stopped according to policy
```

### Scale Up

```text
Replica count increased
Service reconciled
Additional replicas created
Revision association preserved
```

### Scale Down

```text
Replica count decreased
Service reconciled
Excess replicas removed
Revision association preserved
```

### Restore From Revision

```text
Previous app revision selected as restore source
New deployment requested from previous revision template
Orchestrator applies the restored runtime spec
New runtime replicas started next to active revision replicas when needed
Readiness verified
Traffic or endpoint routing switched to restored runtime version
Superseded runtime replicas retained, drained, or stopped according to policy
Restore deployment recorded in app revision history
```

### Delete

```text
Source resource deleted
Deployment deleted
Revisions retained or deleted according to policy
Service deleted
Owned runtime-managed resources deleted
```

## Diagnostics and Inspection

Deployments and revisions should provide diagnostic value.

CloudShell should allow users and tooling to inspect:

* active revision
* previous revisions
* failed revisions
* deployment history
* rollout events
* replica state
* runtime resources created by a revision
* resource changes that caused a revision
* logs and health state associated with revision-owned replicas

Example diagnostic view:

```text
ContainerApp: api
 ├── Deployment
 │    ├── Active Revision: api-r12
 │    ├── Previous Revision: api-r11
 │    └── Status: Healthy
 └── Runtime
      ├── Replica api-1: Running
      ├── Replica api-2: Running
      └── Replica api-3: Running
```

## API and UI Projection

The API should expose deployment and revision information through the orchestrator and Resource Manager projection layer.

Normal resource views may show:

* active revision
* deployment status
* replica count
* rollout status

Advanced views may show:

* deployment details
* revision history
* revision diff
* replica ownership
* runtime-managed resources
* rollout events
* restore actions

Example:

```text
ContainerApp
 ├── Status
 ├── Endpoints
 ├── Dependencies
 ├── Deployment
 │    ├── Active Revision
 │    ├── Rollout Status
 │    └── Revision History
 └── Runtime Resources
```

## Focused MVP Plan

The current implementation already has the internal foundation:

1. Common orchestrator deployment, deployment spec, revision, replica group,
   service tear-down, replica-group tear-down, and deployment tear-down
   description contracts.
2. Separate Resource Manager deployment and orchestration services under
   `CloudShell.ControlPlane.ResourceManager.Deployment` and
   `CloudShell.ControlPlane.ResourceManager.Orchestration`.
3. Resource Manager deployment dispatch for deployment apply, with
   orchestration retaining resource actions and explicit tear-down operations.
4. Default deployment setup of revision-scoped replica groups, routing
   milestones, revision creation, failed-attempt recording, and best-effort
   rollback logging.
5. Provider-owned deployment descriptions let a resource ask Resource Manager
   deployment to apply a runtime state without managing runtime replicas
   directly.
6. Container app image deployment is the first consumer: it records app-owned
   deployment/revision history and asks Resource Manager deployment to apply
   the described runtime state.
7. Provider-owned post-apply cleanup descriptions can identify superseded
   replica groups for explicit tear-down. Container apps use this first for
   superseded local runtime replicas.
8. Active-revision replica scaling uses the replica-group change model.
9. Stable resources and runtime-managed resources can carry deployment,
   service, revision, and replica-group correlation metadata. Container apps
   and their hidden runtime replicas are the first projection path.
10. The container app Deployment tab focuses on deploying an image and reading
    deployment, readiness, rollback, and cleanup events. The Revisions tab
    separately shows the current and previous materialized app states so users
    can inspect versioned outcomes without needing revisions for the initial
    deployment workflow.
11. Post-apply cleanup of superseded replica groups is best-effort. Cleanup
    failures are warning diagnostics on the applied deployment rather than a
    failure of the active revision.

The next MVP changes should stay focused:

1. Keep rollback scoped to tearing down the candidate replica group. Do not add
   automatic restore-to-state or traffic policy machinery for MVP.
2. Add configurable retention and cleanup policies after the basic
   best-effort cleanup model is credible.
3. Add focused tests around failed deployment projection, readiness-gated
   success/failure, post-apply tear-down failure visibility, and extension to
   at least one non-container-app workload shape.
4. Revisit Docker Compose integration only after the default orchestrator and
   first container app path settle; adapters should translate the common model,
   not redefine it.

## Remaining Tasks

* Decide which resources should opt into deployment semantics after container
  apps prove the first path.
* Decide which fields are revision-relevant beyond image and requested
  replicas.
* Define the boundary between scale-only operations that preserve the active
  revision and deployment operations that include requested replica count in a
  new revision.
* Decide how revision-scoped replica groups should be surfaced for richer
  inspection beyond the internal revision outcome and projected resource
  metadata.
* Define how long revisions are retained.
* Define restore behavior for each orchestrator. Restoring to the state
  captured by an old app revision should create a new deployment based on that
  state, not reactivate the old revision in place.
* Implement orchestrator-level `BasedOnRevisionId` tracking, including default
  base selection, persistence on deployment and revision outcomes, and enough
  materialized revision state to author future based-on deployments.
* Define revision merge behavior as a deployment-authoring workflow that
  produces a final deployable state from selected revision state snapshots,
  using deployment records for diff context.
* Define revision diff format.
* Define endpoint cutover, drain behavior, and failure handling beyond the
  current routing milestones and rollback events.
* Define whether revisions are represented as runtime-managed resources or orchestrator metadata.
* Define how deployment events integrate with traceability.
* Define how deployment state is persisted across orchestrator restarts.

## Open Questions

* Should Deployment be represented as a runtime-managed resource, orchestrator metadata, or both?
* Should Revision be represented as a runtime-managed resource, orchestrator metadata, or both?
* Which scale changes are active-revision capacity changes, and which scale
  changes are revision-scoped template changes?
* Should failed revisions remain visible by default?
* Which based-on revision fields should a state-restore deployment and its
  resulting revision expose, and which deployment/change records provide diff
  context?
* Which conflict model should state merge from revisions use when selected
  revision states contain different values for the same resource field?
* How should Docker Compose revisions be represented when Compose has no native revision concept?
* How should deployments be garbage-collected when source resources are deleted?
* Should deployment history be stored in the Resource Manager or orchestrator state?
* How should authorization work for inspecting revisions and runtime-managed resources?
* What minimum deployment behavior must every orchestrator implement?
* Should advanced rollout strategies be part of the first version?
* What is the minimum readiness gate for switching traffic to a new revision
  when no explicit health checks are declared?
* How should deployment and revision events appear in audit logs?
* How should deployment state be reconciled if runtime state has drifted?
