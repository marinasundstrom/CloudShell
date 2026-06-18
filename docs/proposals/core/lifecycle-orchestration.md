# Lifecycle Orchestration Proposal

## Status

Proposed.

CloudShell already has standard lifecycle actions, resource-scoped lifecycle
events, dependency auto-start behavior, and an orchestration service that
routes resource actions to providers. This proposal defines the intended
execution model so future dependency orchestration, event-triggered behavior,
runtime recovery, and extension points build on the same lifecycle contract.

## Problem

Starting a resource is rarely just a provider call on that resource. The
Resource Manager may need to validate authorization, inspect action
capabilities, start dependencies, prepare provider-owned runtime services,
transition resource state, record activity, and surface failures in a way that
users and automation can understand.

Without a common lifecycle procedure, each provider can invent its own flow:

- dependencies may be started without their own activity records
- provider actions may bypass state and capability policy
- failures may not identify the dependency or operation that blocked progress
- future automation cannot reliably react to lifecycle facts
- Resource Manager UI cannot explain what action was requested and what
  lifecycle events resulted

CloudShell needs the orchestration layer to own lifecycle execution semantics
while still letting providers own resource-specific runtime behavior.

## Goals

- Define lifecycle action execution as a common Resource Manager procedure.
- Keep actions and events separate: actions are requested operations; events
  are facts that happened.
- Ensure dependency startup uses the same lifecycle path as directly requested
  startup.
- Record action and lifecycle events on every resource whose action or state
  changes, including dependencies started because another resource was
  requested.
- Keep provider execution behind provider contracts while the Control Plane
  owns authorization, state policy, action capability checks, dependency
  planning, and event recording.
- Leave room for correlation, causation, actor metadata, structured failure
  details, and future event-triggered workflows.
- Establish lifecycle orchestration as an extension point for providers and
  platform components without making MVP behavior event-reactive.

## Non-Goals

- Do not build a general workflow engine for MVP.
- Do not make dependency startup depend on event subscriptions for MVP.
- Do not let providers bypass Resource Manager lifecycle policy for standard
  lifecycle actions.
- Do not require all custom actions to emit lifecycle events. Only standard
  lifecycle action kinds receive Resource Manager lifecycle events
  automatically.
- Do not define full retry, restart, backoff, crash recovery, or durable
  workflow persistence in this proposal. Those policies should build on this
  lifecycle model.

## Current Foundation

The current implementation already has the first pieces of this model:

- standard lifecycle actions: `start`, `stop`, `pause`, and `restart`
- standard action event types under `action.lifecycle.*`
- standard lifecycle event types under `event.lifecycle.*`
- `ResourceOrchestrationService` as the Control Plane action orchestration
  boundary
- dependency auto-start for `Start` actions when dependency policy allows it
- dependency auto-start failure details through the
  `dependencyAutoStartFailed` Control Plane error code
- resource events recorded for requested lifecycle actions and resulting
  lifecycle events
- denied resource actions recorded as warning failed-action resource events
  before provider dispatch is skipped
- provider dispatch through `IResourceOrchestrator` and provider procedure
  contracts

The current path is still intentionally lightweight. Resource events carry
messages and actor text, but do not yet have structured correlation,
causation, failure, dependency path, or orchestration-step metadata.

## Lifecycle Procedure

The standard lifecycle execution path should be:

1. A caller requests a resource action through `IResourceManager`.
2. Resource Manager resolves the target resource and action.
3. Resource Manager evaluates authorization and action capability policy.
4. If authorization denies the action, Resource Manager records a warning
   failed-action event and returns an access-denied error before provider
   dispatch.
5. The orchestration service builds the lifecycle plan for the request.
6. For `Start`, the plan resolves dependencies that must be available.
7. Each dependency that needs startup is started through the same lifecycle
   action path.
8. Resource Manager records the requested action event on each affected
   resource.
9. Resource Manager records the lifecycle transition event before provider
   execution, such as `event.lifecycle.starting`.
10. The selected orchestrator/provider executes the resource-specific work.
11. Resource Manager records the resulting lifecycle event, such as
    `event.lifecycle.started` or `event.lifecycle.start.failed`.
12. Failures are returned as domain-shaped errors with enough context for the
    UI, API clients, and future automation to explain the blocked operation.

Dependency startup is not a hidden provider side effect. If resource A needs
resource B to start first, resource B should receive its own `Start action`,
`Starting`, and `Started` activity records. Resource A should start only after
the dependency plan succeeds.

## Actions and Events

Lifecycle action event types describe requested operations:

- `action.lifecycle.start`
- `action.lifecycle.stop`
- `action.lifecycle.pause`
- `action.lifecycle.restart`

Lifecycle event types describe observed or platform-owned facts:

- `event.lifecycle.starting`
- `event.lifecycle.started`
- `event.lifecycle.start.failed`
- `event.lifecycle.stopping`
- `event.lifecycle.stopped`
- `event.lifecycle.stop.failed`

The activity stream should show both the action and the resulting lifecycle
events. The stable event type remains machine-readable; Resource Manager UI
can map known event types to friendly display names such as "Start action" and
"Started".

Providers can also emit provider-scoped activity while fulfilling a resource
procedure. These events use `event.provider.<provider-id>.*` and are attached
to the resource whose procedure is running. They are useful for implementation
milestones such as resolving a container host, publishing DNS name mappings, or
starting a provider-owned runtime artifact. They are not lifecycle transitions
unless the standard lifecycle event types are used.

## Dependency Execution

Dependency execution should be plan-driven:

- dependencies are resolved from the resource graph
- dependency cycles fail before provider dispatch
- missing dependencies fail before provider dispatch
- dependency auto-start policy decides whether a stopped dependency can be
  started automatically
- action availability checks run before dispatching each dependency action
- dependency failures include the root resource, blocked dependency, dependency
  path, and concrete failure reason

For MVP, this plan can remain in-memory and synchronous inside the action
request. Durable orchestration state, resumable execution, and background
reconciliation are future work.

## Extension Points

Lifecycle orchestration should become an explicit extension point, but the
extension surface should be narrow and ordered around the common lifecycle
procedure.

Potential extension points include:

- provider-facing lifecycle orchestration APIs that let providers contribute
  policy, prerequisites, diagnostics, or follow-up work without owning the core
  lifecycle procedure
- lifecycle plan contributors that can add prerequisites or provider-owned
  preparation steps
- dependency resolvers that can explain non-resource prerequisites through
  diagnostics without hiding them from Resource Manager
- lifecycle policy contributors that can make actions unavailable with
  structured reasons
- event display metadata providers for custom action and event namespaces
- CloudShell extensions that subscribe to resource events and perform
  extension-owned work after lifecycle facts are recorded
- event subscribers or automation triggers that integrate external systems
  after lifecycle facts are recorded
- webhook delivery for external systems that need resource lifecycle
  notifications or automation triggers
- WebSocket or streaming subscriptions for Resource Manager, CLIs, agents, and
  provider tooling that need live operation progress
- runtime recovery policies that decide whether a crashed resource should be
  restarted, left failed, or handed to a provider-native orchestrator

For MVP, lifecycle execution should remain directly orchestrated by Resource
Manager. Event subscribers should not be required for dependency startup,
state transitions, or provider dispatch. Event-triggered workflows should be
additive and should react to recorded facts after the core lifecycle path has
completed or failed.

## Future Event-Triggered Workflows

Resource events are a natural foundation for future automation:

- start a dependent resource after another resource reaches `Started`
- reconcile routing after a container app endpoint changes
- trigger cleanup after a host-scoped resource stops
- publish audit or notification records when a protected action fails
- run provider-specific remediation after a degraded event

This should be modeled as event-driven automation on top of the lifecycle
event stream, not as the core mechanism that makes standard lifecycle actions
work. The core lifecycle path must stay deterministic and explainable even
when no automation engine is installed.

Future event-triggered workflows need additional design for:

- provider API shape and ordering guarantees for in-process orchestration
  contributors
- subscription scope and filtering
- authorization for automation identities
- webhook signing, retry, and delivery state
- WebSocket subscription authentication, fan-out, and backpressure
- retry, backoff, and dead-letter behavior
- idempotency and duplicate event handling
- ordering guarantees and correlation IDs
- split-hosting delivery and persistence
- UI visibility for automation-triggered work

## Correlation and Causation

Lifecycle records should eventually carry structured correlation metadata:

- operation ID for the whole requested lifecycle procedure
- parent operation ID when a dependency action is caused by another action
- actor identity or resource identity that requested the root action
- triggered-by source, such as user, system, startup, dependency, recovery, or
  automation
- root resource ID and current resource ID
- dependency path for dependency-start and dependency-failure records
- provider ID and orchestrator ID for provider dispatch

For MVP, this information can remain partly encoded in event messages. The
logging infrastructure proposal tracks structured resource event fields and
correlation schema work.

## Failure Semantics

Lifecycle failures should distinguish:

- the requested action was not authorized
- the action was unavailable because of state or provider policy
- a dependency could not be found
- dependency auto-start was disabled
- dependency authorization failed
- dependency startup failed
- provider dispatch failed
- provider execution reported failure
- the host shut down or cancellation was requested

The target resource should not emit a successful lifecycle event if a
dependency failed before target provider dispatch. The activity stream should
make clear which dependency blocked the root action. A future structured event
schema should allow this to be queried without parsing event messages.

## MVP Direction

For MVP, keep the lifecycle orchestration scope focused:

1. Route standard lifecycle actions through `ResourceOrchestrationService`.
2. Keep dependency startup plan-driven and non-event-reactive.
3. Record action, authorization-denied, and lifecycle events for dependency
   resources and target resources.
4. Preserve `dependencyAutoStartFailed` as the stable dependency-start failure
   error.
5. Add focused tests when lifecycle action behavior changes.
6. Defer durable workflows, retry policy, background reconciliation, and event
   automation until the core lifecycle flow is stable.

## Open Questions

- Should lifecycle orchestration expose a public plan model, or remain an
  internal Control Plane implementation detail until more providers need it?
- Which correlation fields belong directly on `ResourceEvent` versus a future
  audit or operation-record entity?
- Should dependency startup action events use the same actor as the root
  action, a system actor with root-cause metadata, or both?
- How should cancellation during host shutdown be represented in lifecycle
  events?
- Which lifecycle extension points are safe for third-party providers versus
  platform-only components?
- How should event-triggered automation be authorized when it acts on behalf
  of a user, resource identity, provider, or system principal?
