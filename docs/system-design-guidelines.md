# System design guidelines

This document is the shared design baseline for CloudShell. Use it when adding
features, stabilizing behavior, or deciding where a concept belongs.

For a concept-by-concept explanation of the domain model, abstraction levels,
and API projection, see [Domain model](domain-model.md).

## Product goals

CloudShell is a self-hosted cloud-portal shell for local development,
team-owned platform tooling, and on-premise environments.

The product should make it possible to:

- Register, group, inspect, and operate resources through a consistent shell.
- Keep the WebUI shell deployable separately from Control Plane services.
- Let trusted extensions contribute focused views, resource types, providers,
  logs, procedures, and capabilities without owning the whole application.
- Expose a domain-shaped integration model so consumers do not need to think in
  raw Web API terms.
- Keep provider-owned configuration and platform-owned registration state
  clearly separated.

## Boundaries

The WebUI is the shell surface. The Control Plane owns resource inventory,
registrations, lifecycle procedures, logs, templates, and provider-backed
operational data.

Shell integrations should depend on the cloud-plane client API in
`CloudShell.Abstractions`: domain managers such as `IResourceManager`,
`IResourceTemplateManager`, `ILogManager`, and `ITraceManager`, plus projected
domain entities such as `Resource`. In combined hosts these managers map
to in-process services. In split hosts they map to remote clients. UI and
extension code should not depend directly on internal Control Plane stores,
providers, or generated HTTP clients.

Internal Control Plane services can use lower-level provider and store
interfaces such as `IResourceManagerStore`, `IResourceRegistrationStore`, and
`ILogStore`. These are implementation contracts for the service process, not
the client-side integration model.

## Resource model

`Resource` is the projected domain artifact. It describes what exists now:
identity, type, state, endpoints, dependencies, parentage, details, health, and
provider-declared resource actions.

Resources are composition-based. Do not introduce container, executable,
project, service, or infrastructure subclasses for the projected domain entity.
Use `ResourceClass` for broad classification, `TypeId` for precise
provider/domain identity, `Attributes` for stable non-secret structural facts,
and provider-owned descriptors for execution details. The projected shape stays
uniform; providers decide how each resource class and type behaves.

Keep resource attributes boring and durable. Attribute names use dotted
lower-camel identifiers, values are string-only for MVP, and providers must not
project secrets. Attributes are suitable for generated details, diagnostics,
filtering, and simple orchestration hints; they are not a provider configuration
schema and should not carry structured payloads.

Resource actions are domain operations on a resource. Standard lifecycle
actions use `ResourceActionKind.Run`, `Stop`, `Pause`, and `Restart`. Custom
provider actions use stable IDs. Resource actions are not UI actions.
Use canonical action IDs from the public abstraction for standard lifecycle
actions. `Resource` may provide lookup helpers for those actions, but it
must remain a projected entity; execution is requested through
`IResourceManager`. Client API convenience methods should be manager extensions
that construct domain commands or query capabilities, not active methods on the
resource entity.

API resource responses should expose resource actions as hypermedia
affordances on the projected resource, keyed by action ID. Consumers should be
able to discover the action target and HTTP method from the response instead of
knowing route templates out of band.

Resource action capabilities are separate from resource actions. Capabilities
answer whether a resource action is currently executable and why. They combine
authorization, state, provider support, and other operational constraints.

UI actions are downstream presentation choices. A UI button or menu item may
use a resource action and capability, but the UI element is not the domain
action.

Resource model validation belongs at the model and resource-management
boundaries, not in UI components. A known resource type and its projected
`ResourceClass` must stay consistent across resource type contributions,
creation requests, programmatic declarations, and provider projections. When a
provider or declaration projects a known resource type with the wrong class,
Resource Manager should normalize the projected resource back to the type's
class and expose a diagnostic instead of returning an invalid resource model.

Prefer result objects or diagnostics for expected validation outcomes in domain
APIs. Exceptions are still appropriate for programmer errors and for boundary
adapters that must translate an invalid command into a stable API error, but
the reusable model rule should be expressible without throwing.

## State and capability rules

State-specific behavior belongs in the Control Plane service boundary, not in
individual UI components.

The current lifecycle policy is:

- `Run`: executable for stopped, paused, or unknown resources.
- `Stop`: executable for running, starting, paused, or degraded resources.
- `Pause`: executable for running or degraded resources.
- `Restart`: executable for running, starting, or degraded resources.

Providers may expose action sets based on state, but the Control Plane should
still validate execution against the domain policy before dispatching to the
provider.

When an action is unavailable, return a capability reason suitable for display
or diagnostics. Do not force consumers to infer disabled states from missing
actions alone.

## API and client design

The HTTP API is a versioned contract for the Control Plane, not the product
model itself. Prefer domain-shaped abstractions for consumers, and map those
abstractions to HTTP/OpenAPI inside adapter packages.

Use OpenAPI to document and generate clients for transport details. Keep
consumer-facing code on the domain managers.

Avoid compatibility fields unless they are deliberately part of an upgrade
plan. Once a hypermedia or domain shape is chosen for a new API surface, prefer
one canonical representation.

Boundary validation should happen before internal service calls. Invalid API
payloads should return stable ProblemDetails responses rather than leaking
runtime exception details.

## Ownership

Platform-owned state:

- Resource registrations.
- Resource groups.
- Resource-to-group assignments.
- Resource dependencies declared through registration.

Provider-owned state:

- Provider resource configuration.
- Runtime state, process metadata, container state, logs, and provider-specific
  template payloads.

Templates preserve that split. CloudShell owns the group-level envelope and
orchestration. Providers own per-resource template schemas and validation.

## Testing expectations

Cover behavior at the same layer where it is owned.

- Internal Control Plane state and validation behavior belongs in Control Plane
  service tests.
- HTTP shape, hypermedia affordances, authentication behavior, and remote
  client mapping belong in client/API contract tests.
- Sample viability belongs in sample smoke tests.
- Extension DSL and public resource declarations belong in abstraction tests.

For MVP stabilization, every model change should include at least one test that
guards the intended behavior and one test that guards an important invalid or
edge state.
