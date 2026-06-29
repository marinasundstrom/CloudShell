# CloudShell Terminology

This document is the canonical vocabulary for CloudShell product and domain
concepts. Other docs should link here when they introduce or depend on shared
terms.

## Environment Models

### Environment Resource Model

The **Environment Resource Model** is the lighter model of a host environment.
It describes the resources running in that environment and the relationships
that application developers and Resource Manager users usually need first.

It contains:

- host environment
- resources
- dependencies
- endpoints
- endpoint mappings and names, when present

It answers:

> What resources are running in this environment, and how are they connected?

The graph representation of this model is the **Resource Graph**.

Do not shorten this term to **Resource Model** in contexts where it could be
confused with the lower-level projected resource object model documented in
[Resource Model](resource-model.md).

### Environment Runtime Model

The **Environment Runtime Model** is the fuller management and orchestration
model of the same host environment. It describes how the environment is
actually materialized and operated by Resource Manager, providers, and
orchestrators.

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

The graph representation of this model is the **Environment Map** or, when the
distinction matters, the **Runtime Graph**.

### Environment Artifact

An **environment artifact** is any item that participates in the Environment
Runtime Model.

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

Endpoint mappings can appear in the Environment Resource Model when they help
explain how resources connect. They can also appear as environment artifacts in
the Environment Runtime Model when routing or publishing is managed by the
runtime.

## Runtime and Deployment Terms

### Orchestration Service

An **orchestration service** is an Environment Runtime Model boundary that
groups the artifacts needed to run and operate a runtime service. For a
container app, the orchestration service is the runtime boundary that contains
its replica groups, replicas, routing bindings, and related runtime state.

An orchestration service is not required for every resource. It appears when
several materialized artifacts need to behave as one runtime service.

### Replica Group

A **replica group** is the Environment Runtime Model artifact that represents a
requested set of replicas for a service, usually scoped to a service revision
or runtime version.

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

The **Resource Graph** is the graph representation of the Environment Resource
Model.

### Environment Map

The **Environment Map** is the Resource Manager visualization of the
Environment Runtime Model. It should emphasize service boundaries, replica
groups, replicas, routing, and managed-resource ownership when those details
matter for understanding the running environment.
