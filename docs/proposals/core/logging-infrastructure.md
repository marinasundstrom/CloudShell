# Logging Infrastructure Proposal

## Status

Proposed.

CloudShell currently has two related but separate surfaces:

- provider-owned operational logs through `ILogProvider`, `ILogStore`, and
  `ILogManager`
- platform-owned resource events through `ResourceEvent` and
  `IResourceEventManager`

This proposal exists so CloudShell can evolve logging, resource activity,
audit, diagnostics, metrics, and trace correlation intentionally instead of
treating every operational signal as a text log.

## Problem

CloudShell needs to explain what happened across resources, providers,
Control Plane operations, user actions, workload output, authorization
decisions, deployments, and host/runtime reconciliation.

Not every signal is a text log line:

- process and container stdout/stderr are text-oriented streams
- resource events are actor-attributed platform facts
- audit records may need immutable structured fields
- diagnostics may be state snapshots with remediation hints
- metrics are time-series measurements
- traces are correlated spans
- provider events may contain structured runtime metadata
- future attachments or payloads may be non-text artifacts

If CloudShell models all of these as text logs, the platform will lose
queryability, correlation, authorization boundaries, retention controls, and
clear ownership. If CloudShell over-designs a generic observability platform
too early, MVP work will stall behind decisions that should be made after
concrete providers and Resource Manager workflows prove what they need.

## Goals

- Keep provider operational logs and platform resource events as separate
  concerns.
- Preserve the current provider log abstraction for streamable source logs.
- Make resource activity queryable without forcing it into text log semantics.
- Leave room for structured logging, correlation, audit records, metrics,
  traces, diagnostics, and non-text payloads.
- Define stable vocabulary for sources, actors, resource scope, event type,
  severity, timestamps, and correlation when those fields are needed.
- Keep secrets and credentials out of logs, resource events, diagnostics, and
  projected metadata.
- Support split hosting through domain-shaped managers and Control Plane API
  projections.

## Non-Goals

- Do not replace `ILogger` as the application logging API.
- Do not require every provider log entry to become structured immediately.
- Do not define a full audit retention, export, or compliance system for MVP.
- Do not make metrics, traces, logs, events, and diagnostics share one generic
  payload shape before their usage is proven.

## Current Model

### Operational Logs

Operational logs are provider-owned source streams. Examples include
application stdout/stderr, container logs, provider lifecycle output, and
runtime logs.

The current contracts are:

- `ILogProvider`: provider source contribution
- `ILogStore`: internal aggregation
- `ILogManager`: consumer-facing log listing and reading
- `LogDescriptor`: source descriptor with resource, artifact, and source kind
- `LogEntry`: current text-oriented entry projection

This surface is useful for Resource Manager log views and provider-specific
operational detail.

### Resource Events

Resource events are platform-owned activity records. They describe operations
performed on or because of a resource, including who or what triggered them
when that is known.

Actions and events are related but separate. The activity stream should show
the requested action and the resulting resource event. Standard action event
types for lifecycle operations use the `action.lifecycle.*` namespace, such as
`action.lifecycle.start` and `action.lifecycle.stop`, and carry actor/trigger
information when known. Custom action event types are derived from the action
ID under `action.*`; authors may namespace their own action IDs, for example
`database.backup` becomes `action.database.backup`. Standard lifecycle event
types describe resource lifecycle facts, such as `event.lifecycle.starting`,
`event.lifecycle.started`, `event.lifecycle.stopping`, and `event.lifecycle.stopped`. Authors can
still define custom resource actions and custom resource event types; only
standard lifecycle action kinds receive Resource Manager lifecycle events
automatically.

When dependency startup starts another resource, that dependency gets its own
action and lifecycle records with the dependency-start cause in the message.
For MVP, result and failure details are text on the event message. Structured
result fields, failure data, diagnostics, correlation IDs, and internal
resource-manager state-transition metadata remain future event-schema work.

The current contracts are:

- `ResourceEvent`: platform activity record
- `ResourceEventQuery`: query filters
- `ResourceEventTypes`: standardized action and lifecycle event type constants
- `IResourceEventStore`: internal append/query storage
- `IResourceEventManager`: consumer-facing query API

Resource events may still be projected into an Activity log view for consumers
that open resource logs, but that projection is a compatibility view adapter.
The resource event stream is not conceptually owned by the log provider model.

## MVP Direction

For MVP, implement the smallest useful split:

1. Keep provider logs source-oriented and text-compatible.
2. Persist resource events as platform-owned activity.
3. Query resource events by resource, event type, actor, and time range.
4. Use Resource Manager activity views to render resource events separately
   from raw provider logs. A generated resource Activity tab now reads from
   `IResourceEventManager`.
5. Document audit, structured properties, retention, and export as follow-up
   decisions.

## Future Design Areas

### Structured Log Entries

`LogEntry` may need optional structured fields:

- event ID or stable event name
- category/source
- scopes
- correlation IDs
- resource and artifact references
- severity
- exception summary
- structured attributes

This should be additive and should not force every source to emit structured
data.

### Structured Resource Events

`ResourceEvent` may need structured properties once event schemas are defined.
Candidate fields include:

- operation permission
- request ID
- correlation ID
- result status
- target resource references
- provider ID
- revision ID
- diagnostic code
- authorization decision details

The platform should define schemas for high-value operations before accepting
arbitrary unbounded payloads.

### Event Display Metadata

Resource Manager can map known event types to friendly display names while
keeping the stored event type stable. Standard lifecycle action and event types
use built-in display names. A future Resource Manager UI extension point may
let authors provide display metadata for their own namespaced action and event
types without changing the stored `ResourceEvent` contract.

### Audit Records

Audit may require stronger guarantees than ordinary resource events:

- immutable storage expectations
- retention policy
- actor identity normalization
- before/after references
- authorization decision capture
- export pipeline

Audit records may be built from resource events, but the audit contract should
not be assumed identical until requirements are explicit.

### Metrics and Traces

Metrics and traces should remain separate observability concepts. They can
share correlation identifiers and resource references, but they should not be
forced into the log/event storage model.

### Non-Text Payloads

Some future operational records may point to payloads rather than embedding
text. Examples include diagnostics bundles, screenshots, deployment manifests,
policy evaluation details, or provider-native event documents. The logging
infrastructure should allow references to these artifacts without making the
base log or event entry a blob store.

## Open Questions

- What is the stable minimum structured field set for `LogEntry`?
- Should resource events carry an open attributes bag, typed event schemas, or
  both?
- Which resource event types must be durable audit inputs?
- How should retention differ between source logs, resource events, audit,
  traces, and metrics?
- Which events should be visible to users without manage permission?
- How should OpenTelemetry logs, traces, and metrics map into CloudShell's
  product-shaped managers?
- What correlation ID should connect a UI action, Control Plane request,
  provider operation, resource event, source log, trace span, and audit record?

## Remaining Tasks

- Keep the current MVP resource event persistence/query slice small.
- Add event-type grouping on top of `IResourceEventManager`; the Activity tab
  already supports filtering by event type, actor, and time range.
- Define initial event schemas for resource actions, image deployments,
  lifecycle operations, authorization denials, configuration reads, secret
  reads, and host/runtime reconciliation.
- Decide whether `LogEntry` needs additive structured fields before broader
  provider log work.
- Decide retention and export policy after real Resource Manager usage exists.
