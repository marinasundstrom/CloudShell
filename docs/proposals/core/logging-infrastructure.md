# Logging Infrastructure Proposal

## Status

In progress.

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
- `LogEntry`: text-compatible entry projection with optional structured
  logging metadata

This surface is useful for Resource Manager log views and provider-specific
operational detail.

`LogEntry` keeps the familiar text log shape of timestamp, message, severity,
and source, but now also supports optional structured fields using common logging
and OpenTelemetry terminology:

- `category`: logger or source category, such as `CloudShell.ResourceEvents`
- `eventId`: stable provider or platform event identifier
- `traceId` and `spanId`: correlation with distributed traces when available
- `exceptionSummary`: redacted exception summary text
- `attributes`: string-only structured attributes for query/display metadata

These fields are additive. Providers can continue to emit plain text entries,
and structured fields must not contain secrets or credentials.

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
`event.lifecycle.started`, `event.lifecycle.stopping`, and
`event.lifecycle.stopped`. Standard deployment events use
`event.deployment.*` for resource deployment changes such as image and replica
updates. Authorization-denied resource action attempts use the failed action
event type, such as `action.lifecycle.stop.failed`, with warning severity and
trigger metadata when available. Authors can still define custom resource
actions and custom resource event types under `event.*`; only standard
lifecycle action kinds receive Resource Manager lifecycle events
automatically.

Provider-scoped activity events use the `event.provider.*` namespace. They
are emitted while a provider is fulfilling a resource procedure and are
registered against the resource being operated, not against the provider as a
separate product object. This lets the Activity tab answer "what happened to
this resource?" while still showing which provider implementation produced the
detail. For example, a DNS zone reconcile action can record
`event.provider.cloudshell.platform.dns.nameMappings.publishing` and
`event.provider.cloudshell.platform.dns.nameMappings.published`, and an
application start can record provider details such as container host
resolution or container replica startup.

Provider events are not another action model and should not become a dumping
ground for provider logs. Use them for concise resource-procedure milestones,
outcomes, or observations that help a user understand what the provider did on
behalf of the resource. Provider events must not include secrets, raw
credentials, secret values, or raw configuration values. Keep sensitive
runtime detail in protected provider-owned stores or redacted diagnostics.

When dependency startup starts another resource, that dependency gets its own
action and lifecycle records with the dependency-start cause in the message.
For MVP, result and failure details are text on the event message. Resource
events can carry `traceId` and `spanId` so resource activity, Activity log
entries, and distributed traces can be correlated during local and team-owned
debugging. Structured result fields, failure data, diagnostics, and internal
resource-manager state-transition metadata remain future event-schema work.

The current contracts are:

- `ResourceEvent`: platform activity record
- `ResourceEventQuery`: query filters, including `traceId` and `spanId`
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
3. Query resource events by resource, event type, actor, time range, and
   trace/span correlation.
4. Use Resource Manager activity views to render resource events separately
   from raw provider logs. A generated resource Activity tab now reads from
   `IResourceEventManager`.
5. Document audit, structured properties, retention, and export as follow-up
   decisions.

## Future Design Areas

### Structured Log Entries

The first structured `LogEntry` slice is in place through optional `category`,
`eventId`, `traceId`, `spanId`, `exceptionSummary`, and string-only
`attributes` fields. Resource-event-backed Activity logs populate these fields
when projected through the log view, while stdout/stderr and provider logs can
remain plain text.

Application process logs now parse JSON console log lines at the provider
boundary. The initial supported shape follows the standard
`Microsoft.Extensions.Logging.Console` JSON formatter: `Timestamp`,
`LogLevel`, `Category`, `EventId`, `Message`, `Exception`, `State`, and
`Scopes` are mapped into `LogEntry`, with activity `TraceId` and `SpanId`
picked up from scope data when available. CloudShell also accepts the same
lower-camel field names used by its own persisted structured process-log
lines. Malformed or non-JSON process output remains plain text.

The Project Reference sample uses this path with `ILogger`, JSON console
formatting, activity tracking options, service discovery, OpenTelemetry spans,
and normal structured log properties. A single `/upstream` request now
produces correlated spans and structured application logs for both the
frontend and API resources.

The shell Logs view treats structured metadata as inspectable entry detail, not
as inline text noise. Log source selection lives in the page header, structured
entries can be filtered, and selecting a structured entry shows category,
event, trace/span, exception, and attribute fields in a side pane.

Remaining structured-log work should decide:

- whether CloudShell needs a typed event ID shape instead of the current stable
  string event ID
- how to represent scopes without leaking ambient request or credential data
- which attributes should become indexed query fields
- how OpenTelemetry log records map into `LogEntry` beyond JSON console
  capture

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

The Project Reference sample is the current distributed tracing proving ground.
It uses standard OpenTelemetry ASP.NET Core and HttpClient instrumentation,
CloudShell-injected service discovery configuration, and a sample activity
source so a frontend request produces spans across both web service resources.
The near-term UI direction is Zipkin-style span inspection over retained trace
spans: users should be able to follow a trace across services in a compact
waterfall while CloudShell keeps resource activity, source logs, traces, and
future metrics as separate signals with shared correlation fields.

The target trace detail experience should be service-aware rather than a flat
span table:

- Trace header with trace ID, entry service/operation, duration, total span
  count, and error span count.
- Service legend using stable service/resource colors and basic execution-time
  contribution.
- Nested waterfall view that preserves parent/child span relationships and
  makes cross-service calls visible through positioned duration bars.
- Span details panel with span name, span ID, parent span ID, timing, service,
  resource ID, kind, status, attributes, and events.
- Navigation from a selected span to related resource logs, activity entries,
  and resource details using shared `traceId`, `spanId`, `resourceId`, and
  service name correlation.

Resource pages should keep common operational investigation in context. Events
should have a resource-scoped inline view under the Resource Manager
Management menu because they describe resource-management history. Source logs
and traces now have resource-scoped inline views under a Telemetry menu group
when matching signals exist instead of requiring users to switch to shared
pages for normal per-resource work.

CloudShell should keep the signal taxonomy explicit:

- Telemetry events are application/runtime events emitted by the application or
  its instrumentation pipeline.
- Telemetry metrics are application/runtime measurements such as request
  counts, durations, queue depth, or other instrumented service behavior.
- Resource events are Resource Manager and Control Plane events about resource
  lifecycle, management operations, provider actions, authorization decisions,
  and other management history.
- Resource metrics are provider-observed process/container measurements such
  as CPU usage, memory usage, restarts, or runtime resource consumption.

Telemetry metrics have a standard predefined resource view ID,
`telemetry:metrics`, so providers can contribute a consistent application
metrics tab when application/runtime metric data is available. CloudShell now
has an in-memory metrics manager, list/ingest API, remote-client projection,
and shared/resource-scoped Metrics views. Durable metrics retention,
aggregation, OpenTelemetry metrics ingestion, and provider-owned metrics views
remain separate work.

Telemetry is application/runtime signal investigation, not the same concept as
resource monitoring. Resource monitoring should have a separate
resource-scoped Monitoring tab under the Resource Manager Management group when
a resource provider supports resource metrics for a process or container. That
view has a standard predefined resource view ID, `management:monitoring`, so
providers can contribute a consistent tab when resource monitoring is
available. The detailed plan lives in
[Resource monitoring](resource-monitoring.md). ASP.NET Core resources already support
application-level health checks reported for the resource; CloudShell does not
currently have a separate resource-level health-check model. Monitoring may
summarize those application health checks alongside process/container resource
metrics when useful, but should not blur the line between application health
signals and provider-owned resource metrics. The shared Telemetry trace
explorer remains the cross-resource application telemetry investigation view.

This is an interaction target, not a requirement to copy any specific vendor
UI. CloudShell should keep the view consistent with Resource Manager and should
prefer resource-aware terminology where generic tracing tools expose only
service names.

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
- How should split-hosted UIs subscribe to live telemetry and monitoring data
  through the Control Plane API, and what parts require SignalR/WebSockets
  versus polling fallbacks?
- What correlation ID should connect a UI action, Control Plane request,
  provider operation, resource event, source log, trace span, and audit record?

## Remaining Tasks

- Keep the current MVP resource event persistence/query slice small.
- Keep Activity-tab filtering and action/event grouping focused on
  `IResourceEventManager`; broader event schema and audit decisions remain
  separate.
- Keep resource-scoped Events under Resource Manager's Management menu as
  resource-management history.
- Use the standard `management:monitoring` predefined resource view ID for
  provider-supported resource monitoring tabs. Track the detailed model and
  implementation plan in [Resource monitoring](resource-monitoring.md).
- Keep the current telemetry metrics slice small: in-memory points,
  list/ingest APIs, remote-client support, Metrics views, and Project
  Reference sample request count/duration ingestion are in place. Add durable
  retention, aggregation, OpenTelemetry metrics ingestion, and provider-owned
  Metrics tab implementations separately.
- Keep resource-scoped Logs and Traces under the resource-detail Telemetry menu
  group for routine per-resource investigation.
- Keep shared Telemetry trace exploration available for cross-resource
  investigation, with clear links between selected spans, resource logs,
  activity entries, and the relevant resource detail views.
- Later, design Control Plane-owned streaming subscriptions for live telemetry
  and resource monitoring in split-hosted deployments, including
  authentication, authorization, reconnect, bounded update rates, and
  backpressure behavior.
- Keep resource event trace correlation focused on W3C `traceId`/`spanId`
  fields. Do not turn resource events into trace spans or log records.
- Use the structured `LogEntry` metadata fields for provider logs only when a
  source has real structured data; do not wrap plain stdout/stderr in fake
  structure.
- Define initial event schemas for resource actions, image deployments,
  lifecycle operations, authorization denials, configuration reads, secret
  reads, and host/runtime reconciliation.
- Decide which structured log attributes beyond trace correlation should
  become query filters before broader provider log work.
- Decide retention and export policy after real Resource Manager usage exists.
