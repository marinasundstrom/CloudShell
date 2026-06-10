# Runtime-Managed Resource Proposal

## Status

In progress.

The resource model already supports resource registration, dependencies,
capabilities, actions, and projection through the Resource Manager. However,
there is currently no formal distinction between resources authored and managed
by users and resources created and managed by providers, orchestrators, or
runtime systems.

## Problem

CloudShell resources can create additional runtime artifacts during execution,
deployment, orchestration, scaling, networking, and reconciliation.

Examples include:

* container images
* container instances
* replicas
* volume attachments
* health probes
* service registrations
* runtime endpoints
* gateway configuration
* load-balancer target registrations

These artifacts are often essential to system operation and lifecycle
management, but they are not stable user-facing resources.

Keeping such artifacts outside the Resource Manager creates several problems:

* ownership relationships become implicit
* cleanup becomes difficult
* diagnostics become fragmented
* dependencies become harder to model
* orchestrators must maintain separate state stores
* runtime artifacts cannot participate in the resource graph

CloudShell should track runtime-created resources while preserving a clean
user-facing model.

## Goals

* Model runtime-created artifacts as ordinary resources.
* Distinguish user-managed resources from runtime-managed resources.
* Allow runtime-managed resources to participate in dependencies,
  relationships, actions, capabilities, and diagnostics.
* Keep the normal resource graph focused on user-facing resources.
* Allow advanced tooling and diagnostics to inspect runtime-managed resources.
* Support provider-owned and orchestrator-owned resource creation.
* Preserve ownership relationships between user-managed resources and the
  runtime resources they create.
* Avoid separate runtime state registries where possible.
* Allow runtime-managed resources to be projected selectively by API and UI.

## Non-Goals

* Do not expose every runtime-managed resource in normal UI views.
* Do not require runtime-managed resources to be authorable.
* Do not make runtime-managed resources part of the public application
  contract.
* Do not standardize every runtime artifact type in the first version.
* Do not require all providers to project their internal implementation state.
* Do not expose provider-owned secrets or implementation details as projected
  resource attributes.
* Do not introduce separate runtime-resource storage systems outside the
  Resource Manager.

## Resource Model

A runtime-managed resource is an ordinary `Resource`.

The difference is ownership and lifecycle responsibility rather than entity
shape.

Suggested management modes:

```csharp
public enum ResourceManagementMode
{
    UserManaged,
    RuntimeManaged
}
```

Every resource remains:

* addressable
* identifiable
* related to other resources
* capable of exposing capabilities
* capable of exposing actions
* capable of participating in diagnostics

Runtime-managed resources are created by providers, orchestrators, host
services, or runtime controllers rather than by user declarations.

## Ownership Model

Runtime-managed resources should maintain an explicit ownership relationship.

Examples:

```text
ContainerApp
 └── ContainerImage
```

```text
ContainerApp
 └── Replica
      └── ContainerInstance
```

```text
VirtualNetwork
 └── EndpointMapping
```

```text
LoadBalancer
 └── BackendRegistration
```

Ownership should allow the Resource Manager to determine:

* who created a resource
* who is responsible for reconciliation
* who is responsible for cleanup
* whether a resource should be deleted when its owner is deleted

The relationship should be explicit rather than inferred from naming
conventions or provider state.

## Runtime Resource Examples

Potential runtime-managed resource types include:

* container images
* container instances
* replicas
* deployment revisions
* backend pool registrations
* endpoint registrations
* health probes
* service discovery records
* DNS registrations
* mounted volumes
* runtime certificates
* gateway routes
* traffic-split entries

The exact set should remain extensible and provider-owned where appropriate.

## Visibility Model

Visibility should be independent of ownership.

Suggested visibility modes:

```csharp
public enum ResourceVisibility
{
    Visible,
    DiagnosticOnly,
    Hidden
}
```

Examples:

| Resource       | Management     | Visibility     |
| -------------- | -------------- | -------------- |
| ContainerApp   | UserManaged    | Visible        |
| VirtualNetwork | UserManaged    | Visible        |
| Replica        | RuntimeManaged | DiagnosticOnly |
| ContainerImage | RuntimeManaged | DiagnosticOnly |
| HealthProbe    | RuntimeManaged | Hidden         |

This allows runtime-managed resources to exist within the graph without
cluttering normal user experiences.

## Provider Responsibilities

Providers may create runtime-managed resources as part of reconciliation or
execution.

Examples:

* container providers create images and container instances
* networking providers create endpoint registrations
* load-balancer providers create backend registrations
* deployment providers create revisions and rollout artifacts

Providers remain responsible for:

* runtime-specific configuration
* runtime-specific validation
* runtime-specific cleanup
* runtime-specific state transitions

The Resource Manager remains responsible for:

* identity
* ownership relationships
* dependency tracking
* resource projection
* diagnostics
* action capability reporting

## Orchestrator Relationship

Orchestrators may create runtime-managed resources when materializing a graph.

Examples:

* Docker Compose orchestrator creates container instances
* Kubernetes orchestrator creates workload projections
* host orchestrator creates local proxy resources
* deployment orchestrator creates rollout resources

Orchestrators should not maintain separate runtime graphs when the resources can
be represented as ordinary runtime-managed resources.

The Resource Manager remains the authoritative source of resource identity and
relationships.

## Lifecycle Management

Runtime-managed resources should participate in normal lifecycle operations.

Examples:

### Creation

```text
ContainerApp
 └── creates ContainerImage
```

### Reconciliation

```text
ContainerApp
 ├── Replica A
 ├── Replica B
 └── Replica C
```

### Scale Down

```text
Replica C deleted
```

### Resource Removal

```text
ContainerApp deleted
 └── owned runtime resources deleted
```

Ownership relationships should allow cascading cleanup while preserving
provider-specific behavior.

## Diagnostics and Inspection

Runtime-managed resources are often the most useful resources when debugging.

CloudShell should allow advanced inspection of runtime-managed resources
without requiring them to appear in standard resource views.

Examples:

* replica health
* image version
* container logs
* endpoint registrations
* backend membership
* routing state
* deployment revisions

Diagnostic views should be able to traverse ownership relationships from
user-managed resources to runtime-managed resources.

## API and UI Projection

The API should continue to expose resources through the Resource Manager.

Additional projected fields may include:

* management mode
* visibility mode
* owner resource reference
* creation source
* runtime status

Normal UI views should:

* show user-managed resources
* hide runtime-managed resources by default

Advanced views may:

* show owned runtime resources
* display ownership trees
* display runtime diagnostics
* expose runtime actions

Example:

```text
ContainerApp
 ├── Endpoints
 ├── Dependencies
 └── Runtime Resources
      ├── Image
      ├── Replica #1
      └── Replica #2
```

## Implementation Plan

1. Introduce resource management modes.
2. Introduce runtime-resource ownership relationships.
3. Add runtime-resource registration APIs.
4. Add runtime-resource cleanup and cascading deletion rules.
5. Add API projection for ownership and management metadata.
6. Add declaration and reconciliation tests covering runtime-managed
   resources.
7. Add diagnostics APIs for traversing ownership relationships.
8. Add UI support for runtime-resource inspection.
9. Add provider APIs for runtime-resource creation and reconciliation.
10. Add orchestrator integration for runtime-resource registration.
11. Add lifecycle and cleanup validation tests.

## Remaining Tasks

* Define ownership semantics for shared runtime-managed resources.
* Define garbage-collection behavior for orphaned runtime resources.
* Determine whether ownership should support multiple owners or references.
* Define runtime-resource event and history projection.
* Add provider guidance for runtime-resource registration patterns.

## Open Questions

* Should runtime-managed resources be addressable through the same API routes
  as user-managed resources?
* Should ownership be modeled as a dependency, a dedicated ownership
  relationship, or both?
* Should runtime-managed resources be queryable by default or only through
  diagnostic APIs?
* How should runtime-managed resources participate in authorization checks?
* Should providers be allowed to project partially materialized runtime
  resources during reconciliation?
* How should runtime-managed resources be versioned and audited?
* Should runtime resource visibility be standardized or provider-controlled?
* How should shared runtime resources be represented when multiple user-managed
  resources depend on them?
* Should deployment revisions, container images, and replicas all be modeled as
  runtime-managed resources, or should some remain provider-owned state?
* What diagnostics should be standardized across all runtime-managed resource
  types?
