# Provider-Created and Runtime-Managed Resource Proposal

## Status

In progress.

The resource model already supports resource registration, dependencies,
capabilities, actions, and projection through the Resource Manager. However,
there is currently no formal distinction between how a resource is created,
who is responsible for managing it, how visible it should be, and how its
lifecycle relates to other resources.

Initial implementation now adds these distinctions to the projected `Resource`
shape: source, management mode, visibility, owner resource, and cleanup
behavior. The Control Plane API and remote client preserve those fields, and
Resource Manager hides non-normal resources from the standard inventory while
keeping them available in the loaded graph for parent/detail inspection. This
is an internal foundation for container app runtime artifacts before broad
runtime resource projection is announced as a public product surface.

Runtime-managed resources are one important case, but the broader problem is
that provider-created, orchestrator-created, and runtime-created resources need
to participate in the same resource graph without becoming part of the normal
user-authored application contract.

## Problem

CloudShell resources can cause additional resources to be created during
execution, deployment, orchestration, scaling, networking, provisioning, and
reconciliation.

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

Some of these resources are transient runtime artifacts. Others are durable
provider-created resources, such as deployment revisions, generated container
images, gateway routes, or backend registrations. They may be essential to
system operation and lifecycle management, but they are not necessarily
user-authored resources.

Keeping such artifacts outside the Resource Manager creates several problems:

* ownership relationships become implicit
* cleanup becomes difficult
* diagnostics become fragmented
* dependencies become harder to model
* orchestrators must maintain separate state stores
* runtime artifacts cannot participate in the resource graph

CloudShell should track provider-created and runtime-created resources while
preserving a clean user-facing model.

## Goals

* Model provider-created and runtime-created entities as ordinary resources.
* Provide guidance for deciding when an entity should be represented as a resource.
* Distinguish resource source, management responsibility, lifecycle ownership,
  and visibility as separate qualities.
* Distinguish user-authored resources from provider-created, orchestrator-created,
  and runtime-created resources.
* Allow non-authored resources to participate in dependencies, relationships,
  actions, capabilities, diagnostics, and lifecycle operations.
* Keep normal resource views focused on user-facing resources.
* Allow advanced tooling and diagnostics to inspect provider-created and
  runtime-created resources.
* Support provider-owned, orchestrator-owned, and runtime-controller-owned
  resource creation.
* Preserve ownership relationships between user-managed resources and the
  resources created on their behalf.
* Avoid separate runtime state registries where possible.
* Allow non-authored resources to be projected selectively by API and UI.

## Non-Goals

* Do not expose every provider-created or runtime-created resource in normal UI
  views.
* Do not require provider-created or runtime-created resources to be authorable.
* Do not make non-authored resources part of the public application contract by
  default.
* Do not standardize every provider-created or runtime-created resource type in
  the first version.
* Do not require providers to model every implementation detail as a resource.
* Do not require all providers to project their internal implementation state.
* Do not expose provider-owned secrets or implementation details as projected
  resource attributes.
* Do not introduce separate runtime-resource storage systems outside the
  Resource Manager.

## Resource Qualities

A provider-created or runtime-created resource is still an ordinary `Resource`.
The difference is not entity shape, but a set of independent qualities.

Suggested source modes:

```csharp
public enum ResourceSource
{
    User,
    Provider,
    Orchestrator,
    RuntimeController
}
```

Suggested management modes:

```csharp
public enum ResourceManagementMode
{
    UserManaged,
    ProviderManaged,
    OrchestratorManaged,
    RuntimeManaged
}
```

Source describes where the resource came from. Management mode describes who is
responsible for reconciling, updating, and deleting it. Visibility describes how
it should be projected. Ownership describes lifecycle relationships.

These qualities should not be collapsed into a single category. For example, a
generated deployment revision may be provider-created, provider-managed, and
visible. A replica may be orchestrator-created, runtime-managed, and diagnostic
only. A health probe may be runtime-created, runtime-managed, and hidden.

Every resource remains:

* addressable
* identifiable
* related to other resources
* capable of exposing capabilities
* capable of exposing actions
* capable of participating in diagnostics

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

## Provider-Created and Runtime-Created Resource Examples

Potential provider-created and runtime-created resource types include:

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

## Resource Registration Guidance

Not every provider-managed or runtime-managed implementation detail should be
represented as a resource.

The purpose of the resource model is to track meaningful resources and their
relationships, not to expose every internal object maintained by a provider.

Providers, orchestrators, and runtime controllers should register a separate
resource when one or more of the following conditions apply:

* the entity has an independent lifecycle
* the entity can be created, updated, or deleted independently
* the entity participates in ownership or dependency relationships
* the entity exposes useful diagnostics or operational state
* the entity exposes capabilities or actions
* the entity may require authorization or auditing
* the entity is useful for inspection, troubleshooting, or traceability
* the entity may be referenced by other resources

Providers are not required to register implementation details that do not
provide meaningful value through the resource model.

Examples that are often good candidates for resources:

* deployment revisions
* container images
* replicas
* endpoint registrations
* backend registrations
* mounted volumes
* generated certificates

Examples that may remain provider-owned implementation state:

* temporary reconciliation operations
* internal caches
* protocol-specific connection objects
* transient retry state
* provider-specific optimization data

CloudShell should not require providers to model every implementation detail as
resources. A provider may maintain internal state outside the Resource Manager
when that state does not benefit from resource tracking, ownership, diagnostics,
or lifecycle management.

The decision to register a resource should be based on whether representing the
entity in the resource graph provides meaningful value to operators, providers,
or other resources.

## Visibility Model

Visibility should be independent of source, management mode, and ownership.

Suggested visibility modes:

```csharp
public enum ResourceVisibility
{
    Visible,
    DiagnosticOnly,
    Hidden
}
```

| Resource           | Source       | Management          | Visibility     |
| ------------------ | ------------ | ------------------- | -------------- |
| ContainerApp       | User         | UserManaged         | Visible        |
| VirtualNetwork     | User         | UserManaged         | Visible        |
| DeploymentRevision | Provider     | ProviderManaged     | Visible        |
| Replica            | Orchestrator | RuntimeManaged      | DiagnosticOnly |
| ContainerImage     | Provider     | ProviderManaged     | DiagnosticOnly |
| HealthProbe        | RuntimeController | RuntimeManaged | Hidden         |

This allows runtime-managed resources to exist within the graph without
cluttering normal user experiences.

## Source and Management Model

Source and management mode should answer different questions.

Source answers:

* who or what created this resource?
* did it come from user-authored declarations?
* was it synthesized by a provider, orchestrator, or runtime controller?

Management mode answers:

* who is responsible for reconciliation?
* who may update the resource?
* who is responsible for deletion?
* who owns provider-specific state transitions?

These values may often align, but they should not be required to align.

Example:

```text
ContainerApp
 └── DeploymentRevision
      Source: Provider
      Management: ProviderManaged
      Visibility: Visible
```

```text
ContainerApp
 └── Replica
      Source: Orchestrator
      Management: RuntimeManaged
      Visibility: DiagnosticOnly
```

```text
ContainerApp
 └── HealthProbe
      Source: RuntimeController
      Management: RuntimeManaged
      Visibility: Hidden
```

## Provider Responsibilities

Providers may create provider-managed or runtime-managed resources as part of
provisioning, reconciliation, or execution.

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

Orchestrators may create orchestrator-managed or runtime-managed resources when
materializing a graph.

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

Provider-created and runtime-created resources should participate in normal
lifecycle operations when they are represented in the Resource Manager.

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

Provider-created and runtime-created resources are often the most useful
resources when debugging.

CloudShell should allow advanced inspection of provider-created and runtime-created resources
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
user-managed resources to provider-created and runtime-created resources.

## API and UI Projection

The API should continue to expose resources through the Resource Manager.

Additional projected fields may include:

* source
* management mode
* visibility mode
* owner resource reference
* creation source details
* runtime status

Normal UI views should:

* show user-managed resources
* hide diagnostic-only and hidden resources by default

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

1. Introduce resource source metadata.
2. Introduce resource management modes.
3. Introduce resource visibility modes.
4. Introduce explicit ownership relationships.
5. Add provider-created and runtime-created resource registration APIs.
6. Add cleanup and cascading deletion rules based on ownership.
7. Add API projection for source, ownership, visibility, and management
   metadata.
8. Add declaration and reconciliation tests covering non-authored resources.
9. Add diagnostics APIs for traversing ownership relationships.
10. Add UI support for inspecting provider-created and runtime-created
    resources.
11. Add provider APIs for resource creation and reconciliation.
12. Add orchestrator integration for resource registration.
13. Add lifecycle and cleanup validation tests.

## Remaining Tasks

* Define ownership semantics for shared provider-created and runtime-created
  resources.
* Define garbage-collection behavior for orphaned non-authored resources.
* Determine whether ownership should support multiple owners or whether shared
  relationships should be modeled as one owner plus references.
* Define event and history projection for provider-created and runtime-created
  resources.
* Add provider guidance for resource registration patterns.
* Define whether source and management mode should be extensible by providers.

## Open Questions

* Should non-authored resources be addressable through the same API routes as
  user-authored resources?
* Should ownership be modeled as a dependency, a dedicated ownership
  relationship, or both?
* Should provider-created and runtime-created resources be queryable by default
  or only through diagnostic APIs?
* How should non-authored resources participate in authorization checks?
* Should providers be allowed to project partially materialized resources during
  reconciliation?
* How should provider-created and runtime-created resources be versioned and
  audited?
* Should resource visibility be standardized or provider-controlled?
* How should shared resources be represented when multiple user-managed
  resources depend on them?
* Should deployment revisions, container images, and replicas all be modeled as
  resources, or should some remain provider-owned state?
* What diagnostics should be standardized across all non-authored resource
  types?
