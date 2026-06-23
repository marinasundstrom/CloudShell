# Orchestrator Deployments and Environment Revisions Proposal

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
Most resources primarily represent things that exist in an environment, such as
hosts, networks, volumes, databases, load balancers, configuration stores, or
provider-owned services. They may still be orchestrated for lifecycle and
dependency ordering, but they do not necessarily create a deployment. Workload
resources such as container apps are different when they affect the environment
by asking the orchestrator to create or reconcile runtime resources. Those are
the cases where Resource Manager deployment and environment-revision tracking
become useful.

Container apps are the first motivating workload, but this proposal is not the
container app revision model. It defines how Resource Manager asks an
orchestrator to apply desired runtime state and how the orchestrator records
environment history after apply. Container app configuration revisions,
restore-to-configuration behavior, and app UI terminology belong in the
[Container applications](../containers/container-applications.md) proposal.

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
Workload resources can use the deployment contract to project deployment
status, service id, workload version, requested replicas, and materialized
replica count onto their own resource surfaces. Materialized runtime resources
can also carry the deployment id, service id, and orchestrator revision id for
traceability. In this model, the resource provider describes the desired
runtime state it wants Resource Manager to apply. That deployment targets an
identifiable orchestrator service or standalone runtime scope. The
orchestrator service unit is the runtime boundary that can contain service
routing or loader materialization plus a replica group with the requested
number of replicas for a specific workload version and replicated
configuration.
The Control Plane also records internal orchestrator deployment history for
apply attempts, successful orchestrator revisions, and failed apply results.
When the default orchestrator applies a deployment, it now materializes service
instances with deployment-scoped runtime names so new replicas can be started
next to currently serving replicas before routing is remapped. Replica
materialization can wait for declared startup, readiness, or health checks
before a replica is reported as materialized and before the orchestrator
revision is produced. Superseded runtime instances are described as explicit
replica-group tear-down targets after deployment setup, keeping cleanup
separate from deployment apply.
Providers may keep their own domain deployment or revision history separately
from desired resource definitions. Those records can correlate the domain
request to the orchestrator deployment and environment revision used to
materialize runtime state, but they are not the same entities as orchestrator
deployments or orchestrator revisions. When orchestrator apply fails after a
provider has recorded candidate domain state, Resource Manager can notify the
provider so it can mark that candidate failed and keep the previously active
domain state active.
The intended general rule is broader than container apps: when an orchestrator
handles a resource state change that has runtime workload intent, it may derive
a default deployment for that change even when the user manages the resource
directly rather than managing an explicit deployment resource. That implicit
deployment gives standalone resource changes the same traceability as service
deployments: the current materialized state of a resource can be associated
with the deployment, implicit or explicit, that produced the active environment
revision.
They are not yet a public Resource Manager or Control Plane management surface.
Public APIs for orchestrator-level deployments may be useful later, but they
are not a current use case. Rich rollout history, environment replay, traffic
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
* what environment revision was produced by the change
* how traffic or endpoint routing moved from one runtime group to another

Without orchestrator deployments and environment revisions, several problems appear:

* replica creation becomes orchestrator-specific
* rollout history becomes fragmented
* replay from previous environment state is harder to model
* traceability is incomplete
* resource changes cannot easily be correlated with runtime changes
* default and Docker Compose orchestrators may represent changes differently
* higher-level resources must know too much about runtime orchestration
* runtime-managed sub-resources lack a versioned parent context

CloudShell needs a unified way to represent versioned orchestration changes while keeping the Resource Manager as the authority over the resource graph.

## Goals

* Introduce Deployment as CloudShell's orchestrator-level representation of
  desired runtime state.
* Introduce Orchestrator Revision as CloudShell's environment-history record
  produced by successfully applying an orchestrator deployment.
* Allow an orchestrator to derive a default deployment for a resource state
  change when the resource has runtime workload intent.
* Allow higher-level resources to request orchestration without managing replicas directly.
* Allow orchestrators to compute runtime resources and service replicas from resource intent.
* Support consistent deployment behavior across the default orchestrator, custom
  orchestrators, Docker Compose integrations, Kubernetes integrations, and
  future orchestrator adapters.
* Support traceability from user-managed resources to deployments, environment
  revisions, services, replicas, and runtime-managed resources.
* Enable orchestration-level diagnostics, replay, and environment history
  inspection while allowing higher-level resources to maintain their own
  domain-specific revision models and restore semantics.
* Keep deployments and environment revisions in the orchestrator layer rather
  than making them normal user-authored resources.
* Allow deployments and environment revisions to participate in diagnostics and
  runtime inspection.
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
* Resource-owned deployment history, separate from the desired resource
  definition, so providers can correlate domain requests to the orchestrator
  deployments and environment revisions that materialized them.
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
* Orchestrator-level `BasedOnRevisionId` tracking on deployments and revision
  outcomes. Ordinary deployments default their base to the active or latest
  successful environment revision for the same resource, service, or
  orchestrator scope, while explicit based-on revision ids are preserved for
  replay-like deployments.
* Failed deployment attempts recorded without producing an orchestrator
  revision.
* Provider-owned failure handling when a domain model records candidate state
  before orchestrator apply, so the provider can mark that candidate failed and
  keep its previously active domain state active.
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
* Active runtime replica scaling through the replica-group change model without
  forcing a resource restart or creating a new domain revision when the
  provider treats the change as capacity management.
* Projection of active deployment, requested replicas, materialized replica
  count, deployment service id, orchestrator revision id, and replica-group id
  onto the stable resource and runtime-managed resources. The first projection
  target is the stable container app and its hidden runtime replica resources,
  but the projection is not container-app-specific.
  Requested replicas are the count requested by the resource/provider; the
  orchestrator may materialize fewer replicas when capacity, placement, policy,
  or runtime failures prevent granting the full request.
* Focused tests for successful apply, failed apply, rollback logging,
  deployment with post-apply tear-down, cleanup warning behavior, active
  replica-group scaling, and concurrent deployments for different source
  resources.

The MVP should add or refine next:

* Keep rollback scoped to the candidate deployment unit. Orchestrator
  environment replay is a future operation based on retained environment state
  or revision history; it is not MVP rollback and it does not mutate or
  reactivate an older revision.
* Keep configurable cleanup and retention policy deferred until the basic
  diagnostics and runtime cleanup model has proven useful.

The MVP deliberately leaves these flexible:

* Whether orchestrator deployments and environment revisions become a public
  Control Plane API.
* Whether environment revisions are projected as runtime-managed resources,
  stored only as orchestrator metadata, or both.
* The exact long-term readiness model, traffic shifting model, drain policy,
  retention policy, environment replay workflow, and revision-lineage
  projection.
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
For example, a container app defines container app configuration revisions in
the [Container applications](../containers/container-applications.md) proposal.
Those app revisions are not orchestrator revisions.

An orchestrator revision is an environment-history record. It is produced after
the orchestrator applies a deployment and compares the requested runtime state
to the hosting environment it actually changed. It tracks the deployment that
was applied, the affected service or standalone runtime resources, the
resources created or updated, the resources selected for tear-down, routing or
load-balancer changes, and enough lineage to derive the environment state by
replaying a chain of revisions or by replaying from a checkpoint.

These concepts may be correlated, and either record may reference the other for
traceability, but they do not share identity and they should not be modeled as
the same revision object.

Each subdomain owns the revision data relevant to its own behavior. A
resource-domain revision can point to an orchestrator deployment or
environment revision for traceability, and an orchestrator revision can point
back to the source resource or domain request that caused it. Those references
are correlations, not shared identity.

Orchestrator revisions answer questions such as:

* which deployment changed the hosting environment?
* which desired runtime state was applied?
* which orchestrator environment or runtime target was changed?
* which service or standalone runtime resources were reconciled?
* which runtime-managed resources were created, updated, routed, drained,
  selected for tear-down, or removed?
* which prior environment revision this change follows?
* can the environment state be derived by replaying revisions from a clean
  environment or from a checkpoint?

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
runtime materialization. A future environment replay workflow may also author a
deployment from retained environment state, revision deltas, or a checkpoint,
but that is separate from product-specific restore workflows.

An Orchestrator Revision represents the environment-history outcome of a
successful apply. It is derived after the orchestrator applies the deployment:
the orchestrator records what changed in the hosting environment, which runtime
resources were affected, and how that change follows from the previous
environment revision. This is closer to an environment revision than an app
configuration revision. It should be immutable once recorded. A later
environment replay would author and apply another deployment from retained
environment state or revision history; it would not mutate the old revision in
place.

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
    public ResourceOrchestratorEnvironmentRevisionId? BasedOnRevisionId { get; init; }
    public string ServiceId { get; init; }
    public string RevisionId { get; init; }
    public DeploymentSpec Spec { get; init; }
    public DeploymentStatus Status { get; init; }
}
```

```csharp
public sealed class OrchestratorRevision
{
    public ResourceOrchestratorEnvironmentRevisionId Id { get; init; }
    public string DeploymentId { get; init; }
    public string SourceResourceId { get; init; }
    public ResourceOrchestratorEnvironmentRevisionId? BasedOnRevisionId { get; init; }
    public string ServiceId { get; init; }
    public int RevisionNumber { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string? ProvisionedBy { get; init; }
    public RevisionStatus Status { get; init; }
}
```

The deployment model should represent the orchestrator’s computed workload intent, not the full user-facing resource graph.

An orchestrator revision has a CloudShell-wide unique `Id`. It may also expose
a scoped `RevisionNumber` as a projection for a service, standalone resource,
or environment history view; that number is not the global identity.
`CreatedAt` records when the environment-history record was produced, and
`ProvisionedBy` records who provisioned the deployment that produced it.
Where the implementation needs to prevent identity drift, environment revision
ids should be represented as value objects instead of plain strings. The
deployment `RevisionId` remains separate: for current container app flows it is
runtime/app correlation metadata, not the identity of the orchestrator
environment revision.

For environment-history tracking, the based-on relationship should be
represented as:

```text
Deployment <deployment-id>
  Based on revision <base-revision-id>

Revision <new-revision-id>
  From deployment <deployment-id>
  Based on revision <base-revision-id>
```

The deployment's `BasedOnRevisionId` records which prior orchestrator
environment revision the deployment is intended to follow or derive from. The
resulting revision's `DeploymentId` records the deployment that produced the
environment change, and its `BasedOnRevisionId` records the prior environment
revision used as the base. For ordinary deployments, `BasedOnRevisionId`
defaults to the current active or latest successful environment revision for
that resource, service, or orchestrator scope.

When a deployment is based on an orchestrator revision, its default requested
state can be derived from the environment state produced by replaying that
revision chain or loading an environment checkpoint. The actor may still
include additional resources, overrides, or other deployment input before
apply. In that case the deployment is still based on the selected
orchestrator revision, but the resulting revision records the newly observed
environment change produced by the full deployment request.

This should be implemented at the orchestrator level as metadata and
environment-history tracking, not as a product-specific restore command. The
selected orchestrator should accept and persist `BasedOnRevisionId`, default it
to the active or latest successful environment revision when the caller does
not specify a base, retain enough state or deltas to derive desired runtime
state from revision history, and return the resulting revision with both
`DeploymentId` and `BasedOnRevisionId`. Resource Manager, deployment services,
or provider-owned flows decide when to author an environment replay or
merge-like deployment and which additional deployment input is applied before
materialization.

The same environment-history model should eventually allow replay from a clean
environment or from checkpoints. Replay does not mutate selected revisions and
does not bypass deployment. It is an authoring operation that derives desired
runtime state from retained environment state, revision deltas, and deployment
records, then produces a deployment that can be applied. If that deployment
succeeds, the new orchestrator revision records the environment change and the
revision relationships used to derive it.

### Resource and Orchestrator Boundary

Higher-level resource infrastructure owns the domain request and the
domain-specific history. Orchestrators own runtime rollout mechanics.

Provider or resource infrastructure owns:

* accepting the resource-domain request
* validating domain input such as workload version, configuration, requested
  capacity, ingress intent, identity references, or resource references
* recording domain-specific history when the resource type has its own
  revision or deployment model
* describing the orchestrator deployment that should materialize the requested
  runtime state
* correlating the domain request to the orchestrator deployment and
  environment revision that materialized it

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
changed and which domain state should exist, but it should not directly
manipulate orchestrator-owned replicas, routing tables, backend registrations,
or cleanup behavior when those are part of the orchestrator-managed runtime
scope.

Deployment application is scoped to a resource and deployment. CloudShell must
be able to apply deployments for different source resources concurrently; any
serialization should be limited to the same resource, same runtime target, or
provider-specific critical section that truly cannot be run in parallel.
The deployment model should retain enough status, timing, and revision
correlation for diagnostics. A future live graph of Resource Manager and
orchestrator activity should build on broader event observation rather than
being tied only to orchestrator deployment records. That visualization could
show several deployments progressing at the same time: the orchestrator
materializing runtime resources, updating existing runtime resources, acting on
routing or ingress resources, and cleaning up superseded runtime resources.

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
Environment revision is produced as the history record
        ↓
Service and replicas are reconciled
        ↓
Runtime-managed resources named by the deployment are created or updated
```

Example:

```text
Workload resource
 ├── workload version: v2
 ├── requested replicas: 3
 └── port: 8080
```

may produce:

```text
Deployment: api
 └── Environment revision: env-r12
      └── Service: api
           ├── Replica: api-env-r12-1
           ├── Replica: api-env-r12-2
           └── Replica: api-env-r12-3
```

## Deployment Model

A Deployment represents desired runtime state for an orchestrated service. In
the common path, a stable resource remains individually manageable by Resource
Manager, while the orchestrator generates a default deployment for each
deployment-relevant state or configuration change. This lets CloudShell track
what was requested without requiring the user to author a deployment resource.
At the orchestrator level, the deployment is the primary operational object:
it is what Resource Manager asks the orchestrator to apply. Environment
revisions matter because they record what changed after the deployment was
applied, but they are historical records rather than the main object being
operated.

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
* which orchestrator environment revision recorded the successful apply?
* which runtime resources should be created, updated, retained, or torn down?

## Revision Model

An orchestrator revision represents a successful environment change produced
by applying a deployment. It is not the same kind of snapshot as a container
app revision. The deployment states desired runtime state; after apply, the
orchestrator derives the environment delta that actually occurred and records
that as the environment revision.

A new orchestrator revision should be created when a deployment successfully
changes the hosting environment.

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

Replica count changes need more nuance. A scale operation against the currently
active runtime group can reconcile service capacity while preserving the active
runtime version. It can still produce an orchestrator environment revision if
the hosting environment changed. A deployment operation can also include a
requested replica count for a replacement runtime group, in which case the
orchestrator should materialize that runtime state at the requested capacity
before cutover when the rollout strategy requires availability.

An orchestrator revision should allow CloudShell to determine:

* which deployment produced the environment change
* who provisioned the deployment that produced the environment change
* which prior orchestrator revision logically preceded it through
  `BasedOnRevisionId`
* which orchestrator scope was affected, such as an environment, service,
  standalone resource, or runtime target
* which runtime resources were created, updated, retained, routed, drained,
  selected for tear-down, or removed
* which higher-level resource revision or deployment caused the runtime change,
  when such a correlation exists
* where the revision belongs in chronological order through `CreatedAt` and any
  scoped revision number projection
* whether the hosting environment state can be derived by replaying the
  revision chain from a clean environment or from a checkpoint

### Environment Replay

Environment replay authors an orchestrator deployment from retained
environment state, revision deltas, or a checkpoint, and then records a new
orchestrator revision after the deployment changes the hosting environment.
It does not reactivate or mutate the old orchestrator revision object.

Replay keeps environment revisions immutable while still supporting recovery
from a bad runtime change, such as an invalid routing update, failed resource
configuration, or replicated resource configuration problem. A typical
orchestrator history may look like:

```text
Environment revision env-r12: known-good service routing and replica group
    |
Deployment orch-d13: runtime update with bad routing config
    |
Failed deployment attempt orch-d13, no environment revision produced
    |
Deployment orch-d14: replay desired runtime state from env-r12 plus override
    |
Environment revision env-r14: new environment change, basedOnRevisionId = env-r12
```

Rollback is different. MVP rollback is best-effort cleanup of the candidate
deployment unit when setup fails before activation. Environment replay is an
explicit operation after a previous deployment produced an undesired, failed,
or unhealthy environment state.

Environment lineage can also support merge workflows, but merge semantics
remain future work. A merge would resolve environment-history deltas or
checkpoints into a new orchestrator deployment. The selected revisions are
inputs to authoring that deployment; they are not mutated and they are not
applied directly.

## Service Relationship

CloudShell already has an orchestrator-level Service abstraction.

Orchestrator deployments and environment revisions should build on this
abstraction.

A Service is the logical runtime grouping used by the orchestrator.

A Deployment defines the desired versioned runtime state for that Service.

A Revision represents the concrete materialized outcome of executing that
Deployment.

Within a service, each materialized workload version that needs replicated
runtime instances should have an orchestrator-owned replica grouping. This
grouping is not a Kubernetes ReplicaSet and should not be
introduced as a normal user-authored Resource Manager resource. The structure
can look similar to Kubernetes because it needs to group runtime instances by a
deployment-applied runtime version, but CloudShell should reuse resource
instances as the unit rather than inventing a pod concept. The replica group is the default way the
orchestrator handles service replication. Resource providers and adjacent
resources such as load balancers should consume the orchestrator-managed group
when materializing runtime resources, while still being able to manipulate the
group and its members individually when the target runtime has no higher-level
primitive. It is the runtime boundary that lets the orchestrator track which
replica resources belong to one service runtime version, compare requested and
materialized replica count, observe readiness, remap routing from one
replica group to another, and retire superseded replicas after cutover.

The service, deployment, and revision concepts are intentionally provider
neutral. The default local orchestrator maps them to convention-named
containers and ingress configuration. A Docker Compose orchestrator can map
them to Compose services and generated configuration. A Kubernetes-oriented
orchestrator can map them to Kubernetes workload and service resources. In each
case CloudShell's model remains the same: desired state enters through a
deployment, and the orchestrator records an environment revision for the
resulting hosting-environment change.

Example:

```text
Service: api
 ├── Active runtime version: api-v2
 │    └── Replica Group
 │         ├── api-v2-1
 │         ├── api-v2-2
 │         └── api-v2-3
 ├── Previous runtime version: api-v1
 │    └── Replica Group
 │         ├── api-v1-1
 │         ├── api-v1-2
 │         └── api-v1-3
 └── Routing: active -> api-v2
```

The Service remains the stable runtime grouping primitive. The Replica Group is
the deployment-scoped runtime set inside that service.

Deployment and Revision add versioning, rollout history, and traceability.
During deployment application, the requested runtime state should describe the
stable service, the service routing or load-balancer configuration, and the
replica group required for that runtime version. A deployment of workload X with N
replicas should ask the orchestrator to materialize that runtime state, not ask
the source resource provider to manually own N runtime instances. That lets an
orchestrator start a replacement replica group for a new workload version next
to the currently serving group, update service routing or ingress to the new
group, and then run a separate replica-group tear-down operation to drain or
remove the superseded group. Ordinary active runtime scaling can reconcile the
existing replica group by adding members during setup or tearing members down
when the requested capacity decreases because it is capacity management rather
than workload replacement.

The current deployment-scoped replica names are only the first materialization
of this grouping. The orchestrator revision should retain or reference enough
replica group state so runtime replica resources can be associated with their
deployment service and environment revision across materialization, readiness,
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
Workload resource
 ├── Workload version
 ├── Runtime configuration
 ├── Ports
 ├── Requested replicas
 ├── Dependencies
 └── Runtime requirements
```

The Resource Manager validates the resource and its graph relationships.

The orchestrator applies the deployment, records the environment revision, and
reconciles service state and runtime-managed sub-resources.

This keeps resource definitions focused on intent rather than runtime implementation details.

## Orchestrator Responsibilities

Orchestrators are responsible for:

* creating deployments
* recording environment revisions after successful apply
* reconciling services
* creating and removing replicas
* applying rollout behavior
* applying routing or load-balancer cutover
* draining and cleaning up superseded runtime replicas
* tracking active and previous runtime groups and their environment history
* defaulting deployment `BasedOnRevisionId` to the active or latest successful
  environment revision when no explicit base is supplied
* retaining the environment history, deltas, or checkpoints needed to author a
  later deployment from a prior environment state
* returning revision outcomes with `DeploymentId`, `BasedOnRevisionId`, and
  `ProvisionedBy` for traceability
* reporting deployment status
* reporting revision status
* mapping the common deployment model to backend-specific behavior

Orchestrators should not own product-specific workflows named restore or merge.
They own deployment application, revision outcome recording, based-on revision
metadata, and enough environment history for higher-level Resource Manager or
provider workflows to author a future deployment from prior environment state.

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
Service reconciled
Replica resources created
Containers started through default container host
Environment revision recorded
Runtime group marked active
```

For scale changes:

```text
Deployment updated
Service reconciled
Replica count adjusted
Environment revision recorded if runtime state changed
```

For image or template changes:

```text
Deployment updated
Replacement runtime group created to the desired count next to current replicas
Startup/readiness checks pass for the replacement replicas
Traffic or endpoint routing switches to the replacement group
Old replicas drained or stopped after cutover
Environment revision recorded
Previous runtime group retained or torn down according to policy
```

## Rollout and Availability Semantics

Deployment-relevant updates to a running service should be side-by-side when
availability matters. They should not mutate or restart the active runtime
group in place unless the selected strategy permits disruption. The old runtime
group remains the serving version until the replacement group is ready, unless
the user explicitly chooses a disruptive strategy.

The baseline rollout strategy for a replicated service should be:

1. Apply a deployment from the requested workload version, runtime
   configuration, endpoint, storage, identity, and requested replica settings.
2. Materialize the replacement runtime resources without deactivating the
   current serving group.
3. Wait for the replacement runtime group to provision, reach the desired minimum serving
   capacity, and pass startup/readiness or liveness gates that are relevant to
   routing.
4. Switch traffic, ingress configuration, or endpoint routing to the
   replacement group.
5. Drain or stop the old runtime group according to the rollout and retention
   policy.

This model allows image updates to be combined with a requested replica count.
For example, deploying `api:v2` with three replicas should create a new
runtime group at three replicas, verify that enough replicas are ready, then
switch serving traffic from the old group to the new group. If the new group
fails readiness, the old group remains active and the failed deployment attempt
stays inspectable for diagnostics.

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
failure. Automatic replay to a previously serving environment state is a
separate rollout policy, not the baseline meaning of deployment apply failure.

Advanced strategies such as blue/green, canary percentages, labels, gradual
traffic shifting, and automatic environment replay can build on the same
deployment and environment-revision model. They are not required for the first
implementation, but the baseline model should not force a full resource restart
for image updates.

Scale-only operations remain different. Increasing or decreasing replicas for
the active runtime group reconciles runtime capacity and should not require traffic
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

Orchestrator deployments and environment revisions may create runtime-managed
resources.

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
Source resource
 └── Deployment api
      └── Environment revision env-r12
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

Orchestrator deployments and environment revisions should participate in
orchestration lifecycle operations.

### Initial Deployment

```text
Resource created
Deployment created
Service created
Replicas created
Runtime resources started
Environment revision recorded
```

### Deploy Revision

```text
Runtime-relevant resource intent changed
Deployment updated with new desired runtime state
Replacement replicas started next to active replicas
Replacement readiness verified
Traffic or endpoint routing switched to replacement replicas
Previous replicas retained, drained, or stopped according to policy
Environment revision recorded
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

### Replay Environment State

```text
Prior environment state selected from history or checkpoint
New deployment authored from retained environment state
Orchestrator applies the requested runtime state
New runtime replicas started next to active replicas when needed
Readiness verified
Traffic or endpoint routing switched to replacement runtime group
Superseded runtime replicas retained, drained, or stopped according to policy
Environment revision recorded
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

Orchestrator deployments and environment revisions should provide diagnostic
value.

CloudShell should allow users and tooling to inspect:

* active deployment
* previous environment revisions
* failed deployment attempts
* deployment history
* rollout events
* replica state
* runtime resources affected by an environment revision
* resource changes that caused an environment revision
* logs and health state associated with deployment-owned replicas

Example diagnostic view:

```text
Source resource: api
 ├── Deployment
 │    ├── Active Environment Revision: env-r12
 │    ├── Previous Environment Revision: env-r11
 │    └── Status: Healthy
 └── Runtime
      ├── Replica api-1: Running
      ├── Replica api-2: Running
      └── Replica api-3: Running
```

## API and UI Projection

The API should expose deployment and revision information through the orchestrator and Resource Manager projection layer.

Normal resource views may show:

* active deployment
* active environment revision id
* deployment status
* replica count
* rollout status

Advanced views may show:

* deployment details
* environment revision history
* environment revision diff
* replica ownership
* runtime-managed resources
* rollout events
* environment replay diagnostics

Example:

```text
Source resource
 ├── Status
 ├── Endpoints
 ├── Dependencies
 ├── Deployment
 │    ├── Active Environment Revision
 │    ├── Rollout Status
 │    └── Environment History
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
   the described runtime state. The app-owned revision model itself is defined
   in the container app proposal.
7. Provider-owned post-apply cleanup descriptions can identify superseded
   replica groups for explicit tear-down. Container apps use this first for
   superseded local runtime replicas.
8. Active-revision replica scaling uses the replica-group change model.
9. Stable resources and runtime-managed resources can carry deployment,
   service, orchestrator revision, and replica-group correlation metadata.
   Container apps and their hidden runtime replicas are the first projection
   path.
10. Resource UI surfaces can show deployment, readiness, rollback, cleanup, and
    environment-history events without making users manage orchestrator
    deployment objects directly.
11. Post-apply cleanup of superseded replica groups is best-effort. Cleanup
    failures are warning diagnostics on the applied deployment rather than a
    failure of the active runtime group or active environment revision.

The next MVP changes should stay focused:

1. Keep rollback scoped to tearing down the candidate replica group. Do not add
   automatic environment replay or traffic policy machinery for MVP.
2. Add configurable retention and cleanup policies after the basic
   best-effort cleanup model is credible.
3. Add focused tests around failed deployment projection, readiness-gated
   success/failure, post-apply tear-down failure visibility, and extension to
   at least one non-container-app workload shape.
4. Revisit Docker Compose integration only after the default orchestrator and
   first container app path settle; adapters should translate the common model,
   not redefine it.

## Remaining Tasks

* Decide which resources should opt into deployment semantics after the first
  container app path proves the model.
* Decide which fields are environment-revision-relevant beyond workload version
  and requested replicas.
* Define the boundary between scale-only operations that preserve the active
  runtime group and deployment operations that replace the runtime group.
* Decide how revision-scoped replica groups should be surfaced for richer
  inspection beyond the internal revision outcome and projected resource
  metadata.
* Define how long revisions are retained.
* Define environment replay behavior for each orchestrator.
* Define how much environment history, state, or checkpoint data must be
  retained to author future based-on deployments beyond the current revision
  outcome and replica-group snapshot.
* Define environment-history merge behavior as a deployment-authoring workflow
  that produces a final deployable runtime state from selected history state or
  deltas, using deployment records for diff context.
* Define environment revision diff format.
* Define endpoint cutover, drain behavior, and failure handling beyond the
  current routing milestones and rollback events.
* Define whether environment revisions are represented as runtime-managed
  resources or orchestrator metadata.
* Define how deployment events integrate with traceability.
* Define how deployment state is persisted across orchestrator restarts.

## Open Questions

* Should Deployment be represented as a runtime-managed resource, orchestrator metadata, or both?
* Should Environment Revision be represented as a runtime-managed resource,
  orchestrator metadata, or both?
* Which scale changes are active runtime capacity changes, and which scale
  changes are replacement runtime-group changes?
* Should failed deployment attempts remain visible by default?
* Which based-on revision fields should an environment replay deployment and
  its resulting revision expose, and which deployment/change records provide
  diff context?
* Which conflict model should environment-state merge use when selected
  history states contain different values for the same runtime field?
* How should Docker Compose revisions be represented when Compose has no native revision concept?
* How should deployments be garbage-collected when source resources are deleted?
* Should deployment history be stored in the Resource Manager or orchestrator state?
* How should authorization work for inspecting revisions and runtime-managed resources?
* What minimum deployment behavior must every orchestrator implement?
* Should advanced rollout strategies be part of the first version?
* What is the minimum readiness gate for switching traffic to a replacement group
  when no explicit health checks are declared?
* How should deployment and revision events appear in audit logs?
* How should deployment state be reconciled if runtime state has drifted?
