# Deployments and Revisions Proposal

> This proposal depends on the [Runtime managed resource](runtime-managed-resource.md) proposal.

## Status

In progress.

CloudShell already has a Resource Manager, a resource graph, an orchestrator abstraction, a default orchestrator, a Docker Compose-based orchestrator, and an orchestrator-level service abstraction.

The current orchestration model can start resources and group runtime instances
through services, but there is no formal deployment and revision model for
representing versioned workload changes, rollout history, replica changes, or
traceability across orchestrator implementations.

Initial implementation now adds internal data contracts for
`ResourceOrchestratorDeployment`, `ResourceOrchestratorDeploymentSpec`, and
`ResourceOrchestratorRevision` in the orchestration abstractions. These are
intended for container apps, providers, and orchestrators to build on first.
Container apps now use the deployment contract to project deployment status,
service id, workload version, desired replicas, and materialized replica count
onto the stable app resource and Deployment tab. Materialized runtime replica
resources also carry the deployment id, service id, and deployment revision
they implement for traceability.
The intended general rule is broader than container apps: when an orchestrator
handles a resource state change that has runtime workload intent, it may derive
a default deployment for that change even when the user manages the resource
directly rather than managing an explicit deployment resource.
They are not yet a public Resource Manager or Control Plane management surface,
and rich rollout history, rollback, traffic splitting, and retention remain
deferred.

## Problem

CloudShell resources may express desired runtime behavior that requires orchestration.

Examples include:

* container app replicas
* runtime containers
* service instances
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

Without deployments and revisions, several problems appear:

* replica creation becomes orchestrator-specific
* rollout history becomes fragmented
* rollback is harder to model
* traceability is incomplete
* resource changes cannot easily be correlated with runtime changes
* default and Docker Compose orchestrators may represent changes differently
* higher-level resources must know too much about runtime orchestration
* runtime-managed sub-resources lack a versioned parent context

CloudShell needs a unified way to represent versioned orchestration changes while keeping the Resource Manager as the authority over the resource graph.

## Goals

* Introduce Deployment as a standard orchestrator primitive.
* Introduce Orchestrator Revision as the versioned runtime result of applying an orchestrator deployment.
* Allow an orchestrator to derive a default deployment for a resource state
  change when the resource has runtime workload intent.
* Allow higher-level resources to request orchestration without managing replicas directly.
* Allow orchestrators to compute runtime resources and service replicas from resource intent.
* Support consistent deployment behavior across the default orchestrator and Docker Compose orchestrator.
* Support traceability from user-managed resources to deployments, revisions, services, replicas, and runtime-managed resources.
* Enable orchestration-level rollback, diagnostics, and history inspection while allowing higher-level resources to maintain their own domain-specific revision models.
* Keep deployments and revisions in the orchestrator layer rather than making them normal user-authored resources.
* Allow deployments and revisions to participate in diagnostics and runtime inspection.
* Preserve the existing Resource Manager responsibility for validation, lifecycle coordination, state, ownership, and graph management.

## Non-Goals

* Do not replace the existing orchestrator abstraction.
* Do not replace the existing orchestrator-level service abstraction.
* Do not make Deployment a normal user-authored resource.
* Do not require every resource to use deployments.
* Do not require users to explicitly create or manage deployment resources for
  ordinary resource lifecycle and configuration changes.
* Do not require every orchestrator to implement advanced rollout strategies immediately.
* Do not expose Docker Compose, Kubernetes, or container-host-specific implementation details in the common deployment model.
* Do not move resource graph ownership from the Resource Manager to the orchestrator.
* Do not require resources to manually create their own replicas.
* Do not make deployments responsible for general resource dependency resolution.

## Domain Model

### Revision Terminology

The term `Revision` is used in different CloudShell subdomains and should not
be treated as a single global concept.

A resource type may define its own revision concept as part of its domain model.
For example, a Container App may have Container App revisions that represent
versioned application configuration, traffic behavior, image references,
environment settings, and rollback state.

The orchestrator may also define revisions. An orchestrator revision represents
a versioned runtime realization of a deployment. It tracks the workload shape
that was applied, the service state that resulted, and the runtime-managed
resources associated with that version.

These concepts may be correlated, and they may even share identifiers for
traceability, but they are not the same entity.

```text
Container App Revision
        ↓
causes / maps to
        ↓
Orchestrator Deployment
        ↓
Orchestrator Revision
        ↓
Service + Runtime Resources
```

Each subdomain owns the revision data relevant to its own behavior.

Container App revisions answer questions such as:

* what application configuration changed?
* which image and environment settings are part of this app version?
* which revision receives traffic?
* which revision can the app roll back to?

Orchestrator revisions answer questions such as:

* what workload definition was applied?
* which orchestrator service was reconciled?
* which replicas were created or removed?
* which runtime-managed resources belong to this applied deployment?
* what rollout state resulted?

The deployment and revision model in this proposal refers specifically to
orchestrator deployments and orchestrator revisions unless otherwise stated.

A Deployment is an orchestrator-owned description of an applied workload
change. A deployment may be explicit in a future management surface, but the
MVP path treats it primarily as a default deployment derived by the orchestrator
when a resource state change needs runtime materialization.

An Orchestrator Revision is the immutable runtime version produced when an orchestrator deployment is applied.

Suggested model:

```csharp
public sealed class OrchestratorDeployment
{
    public string Id { get; init; }
    public string OrchestratorId { get; init; }
    public string SourceResourceId { get; init; }
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
    public string ServiceId { get; init; }
    public int RevisionNumber { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public RevisionStatus Status { get; init; }
}
```

The deployment model should represent the orchestrator’s computed workload intent, not the full user-facing resource graph.

## Conceptual Flow

```text
User-managed Resource
        ↓
Resource Manager validates graph and lifecycle
        ↓
Orchestrator receives desired runtime intent or resource state change
        ↓
Default Deployment is created or updated
        ↓
Revision is produced
        ↓
Service and replicas are reconciled
        ↓
Runtime-managed resources are created, updated, or removed
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
           ├── Replica: api-1
           ├── Replica: api-2
           └── Replica: api-3
```

## Deployment Model

A Deployment represents the orchestrator’s versioned workload definition for a
service. In the common path, a stable resource remains individually manageable
by Resource Manager, while the orchestrator generates a default deployment for
each deployment-relevant state or configuration change. This lets CloudShell
track what was applied without requiring the user to author a deployment
resource.

It may include:

* source resource reference
* service reference
* desired replica count
* runtime template
* image reference
* environment configuration
* ports
* mounts
* identity references
* endpoint requirements
* rollout strategy
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

A Revision represents a specific immutable version of a deployment.

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

A revision should allow CloudShell to determine:

* which runtime resources belong to a version
* which version is currently active
* which version was previously active
* which version failed
* which version can be rolled back to
* which resource change caused the revision

## Service Relationship

CloudShell already has an orchestrator-level Service abstraction.

Deployments and revisions should build on this abstraction.

A Service is the logical runtime grouping used by the orchestrator.

A Deployment defines the desired versioned workload for that Service.

A Revision represents a concrete version of that Deployment.

Example:

```text
Service: api
 ├── Active Revision: api-r12
 ├── Previous Revision: api-r11
 └── Replicas:
      ├── api-1
      ├── api-2
      └── api-3
```

The Service remains the runtime grouping primitive.

Deployment and Revision add versioning, rollout history, and traceability.

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

## Orchestrator Responsibilities

Orchestrators are responsible for:

* creating deployments
* producing revisions
* reconciling services
* creating and removing replicas
* applying rollout behavior
* tracking active and previous revisions
* reporting deployment status
* reporting revision status
* mapping the common deployment model to backend-specific behavior

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
New replicas created
Old replicas drained or stopped
Revision marked active
Previous revision retained for history
```

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

### Update

```text
Resource changed
Deployment updated
New revision created if revision-relevant fields changed
Service reconciled
Runtime resources updated
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

### Rollback

```text
Previous revision selected
Deployment updated to previous revision template
Service reconciled
Runtime resources replaced or adjusted
Previous revision marked active
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
* rollback actions

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

## Implementation Plan

1. Define common Deployment and Revision domain types.
2. Define deployment status and revision status models.
3. Define how deployments relate to orchestrator services.
4. Define how deployments relate back to source resources.
5. Add orchestrator APIs for creating, updating, applying, and deleting deployments.
6. Add revision creation rules for deployment-relevant changes.
7. Add active revision tracking.
8. Add revision history storage.
9. Integrate deployments with the default orchestrator.
10. Integrate deployments with the Docker Compose orchestrator.
11. Associate replicas and runtime-managed resources with revisions.
12. Add diagnostics APIs for deployment and revision inspection.
13. Add Resource Manager projection for active deployment state.
14. Add lifecycle tests for create, update, scale, rollback, and delete.
15. Add UI support for active revision and revision history.
16. Add traceability events for deployment and revision changes.

## Remaining Tasks

* Define the exact boundary between Deployment, Service, and Resource.
* Decide which fields are revision-relevant.
* Define whether scale changes always create revisions.
* Define how long revisions are retained.
* Define rollback behavior for each orchestrator.
* Define revision diff format.
* Define failure handling during rollout.
* Define whether revisions are represented as runtime-managed resources or orchestrator metadata.
* Define how deployment events integrate with traceability.
* Define how deployment state is persisted across orchestrator restarts.

## Open Questions

* Should Deployment be represented as a runtime-managed resource, orchestrator metadata, or both?
* Should Revision be represented as a runtime-managed resource, orchestrator metadata, or both?
* Should scaling create a new revision, or should only template changes create revisions?
* Should failed revisions remain visible by default?
* Should rollbacks create new revisions or reactivate previous revisions?
* How should Docker Compose revisions be represented when Compose has no native revision concept?
* How should deployments be garbage-collected when source resources are deleted?
* Should deployment history be stored in the Resource Manager or orchestrator state?
* How should authorization work for inspecting revisions and runtime-managed resources?
* What minimum deployment behavior must every orchestrator implement?
* Should advanced rollout strategies be part of the first version?
* How should deployment and revision events appear in audit logs?
* How should deployment state be reconciled if runtime state has drifted?
