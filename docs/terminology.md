# CloudShell Terminology

This document is the canonical vocabulary for CloudShell product and domain
concepts. Other docs should link here when they introduce or depend on shared
terms.

## Environment Models

### Host Environment

The **host environment** is the environment CloudShell manages. It is where the
complete realized model exists: resources, relationships, runtime artifacts,
operational state, deployment history, and the provider or orchestrator facts
Resource Manager can use to explain or operate that environment.

A host environment may be local, self-hosted, team-managed, or backed by a
provider integration. The term describes the managed environment, not the
CloudShell web host process.

### Realized Model

The **realized model** is the complete model of a host environment as known by
CloudShell. It includes the Resource model and the Runtime model together.

The Resource model is the resource-focused subset of the realized model. The
Runtime model is the fuller management, orchestration, and deployment view of
the same realized environment.

### Resource Model

The **Resource model** represents the resources in a host environment and the
relationships between them. It is the part of the realized model that
application developers and Resource Manager users usually need first.

It contains:

- resources
- dependencies
- endpoints
- endpoint mappings and names, when present

It answers:

> What resources are running in this environment, and how are they connected?

The graph representation of this model is the **Resource graph**.

This term intentionally overlaps with the lower-level projected resource object
model documented in [Resource Model](resource-model.md). Use **Resource graph**
when the graph representation needs to be distinguished from individual
resource records, projections, or definitions.

### Runtime Model

The **Runtime model** is the fuller management, orchestration, and deployment
model of the same host environment. It includes the Resource model as a subset
and adds the runtime artifacts that explain how resources are materialized,
operated, versioned, routed, and changed over time.

It contains:

- environment artifacts
- resources
- orchestration services
- replica groups
- replicas
- routing bindings
- retained or superseded runtime revisions
- environment revisions

It answers:

> How is the environment realized, versioned, deployed, scaled, routed, and
> operated?

The Runtime model is where CloudShell talks about host state, orchestration,
deployments, service boundaries, replica groups, replicas, routing bindings,
retained revisions, and environment revisions. The graph representation of
this model is the **Environment Map** or, when the distinction matters, the
**Runtime graph**.

### Environment Artifact

An **environment artifact** is any item that participates in the Runtime model.

Examples:

- resource
- orchestration service
- replica group
- replica
- routing binding
- load-balancer route
- endpoint mapping
- retained previous revision

Environment artifacts are not only deployment internals. A deployment may
define, update, replace, or retire environment artifacts, but the artifacts
exist as part of the realized environment and can be versioned by environment
revisions.

### Environment Revision

An **environment revision** is a versioned record of realized environment
artifacts after a runtime change has been applied.

Environment revisions can record changes to resources, services, replica
groups, replicas, routing bindings, and other materialized runtime state.

## Resource Terms

### Resource

A **resource** is the CloudShell management artifact. It is what the Control
Plane registers, authorizes, groups, displays, operates, and exposes through
Resource Manager and the Control Plane API.

### Service

A **service** is the runtime or infrastructure capability contained in or
provided by a resource, such as a Web API, SQL Server process, DNS publisher,
load-balancer runtime, identity provisioner, configuration API, or Secrets
Vault API.

Use **resource** for the thing CloudShell manages. Use **service** for the
capability, process, API, or runtime behavior behind that resource.

### Service Resource

A **service resource** is the specific `cloudshell.service` resource kind. It
can model a service facade, service unit, imported service, or advanced
routing target. It is not a synonym for every resource that provides a
service.

### Endpoint

An **endpoint** is the resource-level contract for a network-addressable
capability. It describes the named port, protocol, and exposure intent or
observed shape associated with a resource.

### Endpoint Mapping

An **endpoint mapping** connects an endpoint to a concrete reachable address,
published name, route, or other environment-specific access path.

Endpoint mappings can appear in the Resource model when they help explain how
resources connect. They can also appear as environment artifacts in the Runtime
model when routing or publishing is managed by the runtime.

## Runtime and Deployment Terms

### Orchestration Service

An **orchestration service** is a Runtime model boundary that groups the
artifacts needed to run and operate a runtime service. For a
container app, the orchestration service is the runtime boundary that contains
its replica groups, replicas, routing bindings, and related runtime state.

An orchestration service is not required for every resource. It appears when
several materialized artifacts need to behave as one runtime service.

### Replica Group

A **replica group** is the Runtime model artifact that represents a requested
set of replicas for a service, usually scoped to a service revision or runtime
version.

CloudShell uses **replica group** as the durable term. It is intentionally not
named after a Kubernetes ReplicaSet, even though it solves a similar class of
problem.

### Replica

A **replica** is a materialized runtime artifact inside a replica group. For a
container app, a replica is usually a runtime container resource that occupies
a requested replica slot.

### Deployment Definition

A **deployment definition** is a desired runtime change. It may define, update,
replace, or retire environment artifacts such as services, replica groups,
replicas, routing bindings, or resource runtime state.

Deployment definitions are not part of user-facing resource template authoring
by default. Users express resource intent through resource definitions; Resource
Manager and orchestrators can translate accepted resource intent into
deployment definitions when runtime realization requires it.

### Resource Graph

The **Resource graph** is the graph representation of the Resource model.

### Environment Map

The **Environment Map** is the Resource Manager visualization of the Runtime
model. It should emphasize service boundaries, replica groups, replicas,
routing, and managed-resource ownership when those details matter for
understanding the running environment.

## Deprecated Term Aliases

These aliases may appear in older docs and code comments while the POC is being
renamed:

- **Host Environment Model** means the realized model scoped to a host
  environment.
- **Environment Resource Model** means the Resource model.
- **Environment Runtime Model** means the Runtime model.
- **Runtime Graph** means the runtime graph representation used by the
  Environment Map.
