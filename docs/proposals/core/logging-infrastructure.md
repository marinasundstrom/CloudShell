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

Application provider process logs are memory-only by default. Hosts can opt
into provider-owned plain files through `Observability:ApplicationLogs`,
including retention by age, retention by entry count, and an optional
per-day file split. The file split remains a storage choice; resource event
logs are a separate platform activity stream and should not be controlled by
application log file settings.

The current contracts are:

- `ILogProvider`: provider source contribution
- `ILogStore`: internal aggregation
- `ILogManager`: consumer-facing log listing and reading
- `LogDescriptor`: source descriptor with resource, artifact, and source kind
- `LogEntry`: text-compatible entry projection with optional structured
  logging metadata

This surface is useful for Resource Manager log views and provider-specific
operational detail.

The current model is missing an explicit source concept. The intended next
domain shape is:

- `ResourceLogSource`: resource-model discovery metadata that declares a log
  produced by or on behalf of a resource.
- `LogSource`: Control Plane projection of a log source that can be listed,
  authorized, queried, read, streamed, and rendered.
- `ILogProvider`: integration point that contributes and accesses projected
  log sources, rather than being conceptually "the log" itself.
- parser/format metadata: a separate concern that tells CloudShell how to
  interpret records from a source when a provider does not return fully shaped
  entries.

`ResourceLogSource` belongs to the resource model. It is a discovery contract:
CloudShell and the Control Plane use it to discover which logs a resource
produces, which are provider defaults, which are custom, where they come from,
and how they can be accessed. It describes source kind, format, display name,
capabilities, origin, purpose, configuration metadata, and the provider-owned
source location or capture target, such as stdout, stderr, a file path, file
pattern, container runtime stream, sidecar, hidden sub-resource, or external
provider API. A process-backed application can get implicit stdout/stderr
sources, while the resource can declare additional sources such as ASP.NET
Core file-sink logs. A visible resource can also declare sources physically
produced by multiple background processes, containers, or hidden sub-resources
without exposing those implementation details as primary Resource Manager
items.

Resource log source configuration is capability driven. A resource or resource
type that supports log source declaration/configuration should advertise
`ResourceCapabilityIds.LogSources`. The UI should use that capability plus the
source configuration metadata to decide whether users can add, remove, or edit
sources. Provider defaults, user-configured sources, programmatic extension
declarations, provider projections, and runtime-discovered sources have
different origins and should remain distinguishable.

`LogSource` is the Control Plane abstraction projected from resource-owned
sources and any provider-owned non-resource sources. It is the object that log
listing, read, query, and stream APIs should address. The initial abstraction
skeleton is in `CloudShell.Abstractions.Logs`; current `LogDescriptor` values
can carry compatible source metadata and project to `LogSource` while existing
APIs and UI keep using descriptors. This lets the Logs UI group by resource
while still supporting future provider pages or global log views that are not
strictly resource-scoped.

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

### Runtime Logging Categories

CloudShell still uses `ILogger` for host and framework runtime diagnostics.
These logs are separate from provider-owned operational logs and platform
resource events. Runtime logger categories should use stable CloudShell-owned
category names from `CloudShell.Abstractions.Logging.CloudShellLogCategories`
when the category is part of an operational area that operators may reasonably
filter, such as Resource Manager startup, shutdown, and health polling.

Prefer categories rooted at `CloudShell`, for example:

- `CloudShell.ControlPlane.ResourceManager.ResourceHealth.Polling`
- `CloudShell.ControlPlane.ResourceManager.ResourceHealth.Probes`
- `CloudShell.ControlPlane.ResourceManager.ProgrammaticResourceStartup`
- `CloudShell.ControlPlane.ResourceManager.HostScopedResourceShutdown`
- `CloudShell.ControlPlane.ResourceManager.Lifecycle`
- `CloudShell.ControlPlane.ResourceManager.LocalProcess`

Keep the initial category set strict. Resource Manager lifecycle and local
process diagnostics that are useful in the console belong in stable CloudShell
categories. These logs must be controlled by standard log-level configuration
rather than hard-coded environment checks, so a host can force them on outside
Development or keep them quiet in Development. Narrow component warnings, UI
recovery logs, and sample application logs can keep their existing type or
sample categories until there is an operator-facing filtering reason to promote
them.

Resource lifecycle logs should be emitted at the Resource Manager orchestration
boundary, where resource actions are requested, completed, and failed. Providers
should use provider events for detailed procedure milestones and only emit
runtime logs for provider-owned process diagnostics.

When CloudShell creates framework clients for a specific operational area, use
named clients so framework diagnostics land under a dedicated category. Resource
health probes use the named client
`CloudShell.ControlPlane.ResourceManager.ResourceHealth.Probes`, which maps the
default `HttpClient` diagnostics under
`System.Net.Http.HttpClient.CloudShell.ControlPlane.ResourceManager.ResourceHealth.Probes`.
Hosts can filter that category without muting unrelated HTTP clients.

Do not log routine health-check status changes as application log noise. The
Control Plane stores health results as resource health snapshots, and Resource
Manager reads those snapshots for health status. Runtime logs should be
reserved for unexpected polling infrastructure failures, and repeated failures
should be suppressed until polling succeeds again.

Local development hosts can tune these categories through standard appsettings
logging configuration:

```json
{
  "Logging": {
    "LogLevel": {
      "CloudShell.ControlPlane.ResourceManager.ProgrammaticResourceStartup": "Information",
      "CloudShell.ControlPlane.ResourceManager.HostScopedResourceShutdown": "Information",
      "CloudShell.ControlPlane.ResourceManager.ResourceHealth.Polling": "Warning",
      "System.Net.Http.HttpClient.CloudShell.ControlPlane.ResourceManager.ResourceHealth.Probes": "Warning",
      "CloudShell.ControlPlane.ResourceManager.Lifecycle": "Information",
      "CloudShell.ControlPlane.ResourceManager.LocalProcess": "Information"
    }
  }
}
```

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
5. Introduce `ResourceLogSource` and projected `LogSource` before adding more
   log storage/query surface area, so resources can declare multiple log
   sources and providers can expose their read/stream/query capabilities
   consistently.
6. Document audit, structured properties, retention, and export as follow-up
   decisions.

## Future Design Areas

### Resource Log Sources

Resource log sources should become the durable resource-model declaration for
logs. They should allow providers and resource declarations to describe:

- whether a source is implicit, such as captured stdout/stderr for a running
  process, or explicit, such as an ASP.NET Core file sink
- the source kind, such as process stream, file, file pattern, container
  runtime stream, external API, or provider-owned source
- the source format, such as plain text, JSON console, Serilog compact JSON,
  OpenTelemetry log data, or provider-shaped structured entries
- whether the source supports read, tail/stream, query, search, or structured
  field filtering
- retention and persistence policy hints, including whether storage is
  session-only, provider-file backed, database backed, or remote-provider
  backed

The Control Plane should project these declarations into `LogSource` records
that are independent of the physical producer. A single visible resource may
project sources produced by multiple processes, containers, sidecars, hidden
sub-resources, or provider subsystems.

### Structured Log Entries

The first structured `LogEntry` slice is in place through optional `category`,
`eventId`, `traceId`, `spanId`, `exceptionSummary`, and string-only
`attributes` fields. Resource-event-backed Activity logs populate these fields
when projected through the log view, while stdout/stderr and provider logs can
remain plain text.

Structured logging is a format and capability of a `LogSource`, not a separate
top-level observability category. A file, process stream, container log stream,
or provider API can all emit either plain text or structured records. The
parser/format metadata on the source should determine how CloudShell converts
raw records into the common fallback `LogEntry` projection or a richer
provider-specific view model.

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

For multi-instance resources, Logs should stay scoped to the stable managed
resource by default. When multiple telemetry scopes are observed, the view
should add an `All instances` default plus provider-defined scope options. A
single observed scope should not add selector chrome. Implementing that
consistently requires log entries or descriptors to carry scope dimensions
such as scope resource ID, scope name, scope kind, and any provider-specific
details such as replica ordinal, container name, or deployment revision.

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

Multi-instance resources should also keep traces scoped to the stable managed
resource by default. A scope selector can narrow spans to one observed
provider-defined scope when that is useful, but trace identity remains
trace-first and service-aware; a distributed trace may cross services and
scopes. Telemetry metric views should likewise default to resource-level
aggregates and expose per-scope filtering or breakdowns only when metric
points carry stable scope dimensions.
Provider-observed CPU, memory, restart count, uptime, and materialization
state remain Resource Metrics under Management > Monitoring.

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
has trace and metric list/ingest APIs, remote-client projection,
shared/resource-scoped Metrics views, appsettings-configured resource metric
panels for live indicators and retained recent-history line charts, and an
opt-in database telemetry store for traces and metric points with per-resource
retention limits. Control Plane aggregation, OpenTelemetry metrics ingestion,
and provider-owned metrics views remain separate work. Application source-log
file persistence is now provider-owned and opt-in, with bounded retention.

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

Health has two distinct UI targets. A resource-scoped Health tab should answer
questions about the selected resource and its configured checks: current
status, recent polling history, and when a check degraded. The common Health
workspace can later become a system-health summary similar to a status page,
with timeline rows for explicit health scopes and drill-down into affected
resources and checks. That future view should not rely on ordinary resource
groups alone, because operators may need to define health scopes such as a
service, product area, capability, tenant environment, or other system slice.
Incident comments, subscriptions, and curated public summaries are related but
separate incident-management features and should remain outside the immediate
MVP.

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
