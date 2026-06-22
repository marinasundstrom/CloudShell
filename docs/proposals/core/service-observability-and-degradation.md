# Service Observability and Degradation Proposal

## Status

Proposed.

This proposal defines the MVP service-level observability experience for local
development. It connects existing logs, traces, telemetry metrics, health, and
resource monitoring into a resource-centered degradation view without turning
CloudShell into a full observability platform.

## Problem

Developers running local CloudShell environments need to understand whether a
service is healthy under load and where degradation comes from. The current
signals exist in separate places:

- request traces and error spans
- structured application logs
- telemetry metric points
- resource health
- process/container monitoring snapshots
- container app replica projections
- service resources that front other resources

Those signals are useful individually, but the local-development workflow needs
an overview that correlates them by resource, service, route, replica, and
time window.

The first priority is local services that run as executable, ASP.NET Core
project, container-backed, or container app resources. Some services run one
instance. Others run replicas where each replica should offload part of the
work. Users should inspect the stable service or application resource first,
then drill into a replica only when imbalance, resource pressure, errors, or
placement explains the degradation.

CloudShell should also leave room for provider-backed cloud resources and
remote container hosts. CPU and memory are not universal capacity facts: a
local process is relative to the developer machine, a local Docker container
is relative to the local Docker host, a remote Docker container is relative to
that remote host, and future orchestrators may report node or runtime-specific
capacity.

## Goals

- Provide a service-first overview that answers "what is degraded?" before
  asking users to choose logs, traces, metrics, or monitoring manually.
- Keep the stable service, container app, or application resource as the
  primary investigation entry point.
- Treat replicas as runtime scopes of a service, not as the default top-level
  navigation target.
- Show load indicators such as requests per second, minute, or hour over an
  explicit time window.
- Show recent exceptions and error frequency by resource, route, exception
  type, status code, and replica scope when those dimensions are available.
- Correlate degraded routes and error spans with structured logs and trace
  details using trace/span identifiers and resource references.
- Show resource pressure and capacity context from provider-owned monitoring
  snapshots, including the runtime host or provider that makes CPU and memory
  values meaningful.
- Support local development first while keeping the model compatible with
  remote Docker hosts and later cloud-backed resources.
- Allow a redacted public report that summarizes current degradation evidence
  without exposing secrets.
- Provide base Control Plane query/retrieval surfaces and common Resource
  Manager views for telemetry and metrics so local development works without
  requiring a separate observability stack.

## Non-Goals

- Do not replace Prometheus, Grafana, Seq, Datadog, or a production incident
  management system. CloudShell should provide resource-centered correlation
  and local-development workflows, while standards-based observability systems
  can remain specialized backends or companion tools.
- Do not require a durable time-series database for MVP.
- Do not make CPU, memory, or host capacity a universal service-level signal
  without provider and placement context.
- Do not make runtime replicas the primary resource-management identity.
- Do not infer broad environment uptime or status-page percentages from
  ordinary resource groups.
- Do not expose secrets, credentials, protected configuration values, or
  sensitive payloads in reports.
- Do not merge logs, traces, metrics, health, and resource monitoring into one
  generic storage model.

## Conceptual Model

### Service Observation Target

A service observation target is the stable resource a user expects to inspect
first. It can be:

- an application resource
- a container app resource
- a `cloudshell.service` resource that fronts one or more backing resources
- a future provider-backed managed service resource

The target owns the service-level summary: load, error rate, degraded paths,
recent exceptions, dependency symptoms, health, and correlated signal entry
points.

### Runtime Scope

A runtime scope is an observed execution unit under the target. Examples
include:

- a local process
- a container app replica
- a runtime container
- a remote container instance
- a future orchestrator replica or task

Runtime scopes are used for filtering and breakdowns. They should appear as
"All instances" by default, with per-scope drill-down when more than one
runtime scope exists.

### Load

Load describes service traffic over a time window. The MVP should derive load
from telemetry metrics and/or retained spans:

- requests per second
- requests per minute
- requests per hour
- requests by route
- requests by status-code class
- request distribution across replicas

Load panels must state their time window. A raw cumulative counter is not a
load signal by itself.

### Degradation

Degradation is a correlated finding over recent signals. Initial findings can
be simple and explainable:

- error rate increased for a route
- recent exceptions occurred
- one route is slower than recent traffic
- one replica has a disproportionate error share
- one replica is resource constrained while peers are not
- a dependency call is failing or slow in traces
- resource health is degraded
- provider monitoring is unavailable for a resource that normally exposes it

Findings should link to the evidence: traces, logs, metric points, monitoring
snapshots, health checks, and resource details.

### Capacity Context

Capacity context explains where resource pressure is measured:

- local process on developer machine
- local Docker host
- remote Docker host
- future orchestrator host/node/runtime
- provider-backed cloud resource

Resource monitoring values should display provider and placement context
instead of implying that CPU or memory has the same meaning everywhere.

### Public Report

A public report is a redacted snapshot of service status and recent evidence.
It should be generated intentionally by a user with sufficient access. The
first shape can be static and human-readable:

- service/resource identity and labels
- current health and degradation summary
- recent load and error-rate windows
- top degraded routes
- recent exception counts by type
- replica balance and resource-pressure summary
- selected trace/log references or summarized excerpts
- provider/host context for resource monitoring
- explicit redaction note

Reports should not include secrets, raw protected configuration, credentials,
authorization tokens, or unbounded log dumps.

## Resource Manager UX

CloudShell should provide common views for retrieving and displaying retained
telemetry, metrics, logs, traces, health, and resource monitoring data. Those
views are the default local-development experience and the common shell surface
for extensions. Specialized backends can still provide storage, aggregation,
or production dashboards behind the same domain-shaped query contracts.

### Service Overview

The service, application, or container app overview should show compact
operational signals:

- current health
- current load window
- error rate
- recent exception count
- hottest degraded route or dependency
- replica balance when replicas exist
- resource pressure summary with provider/host context

The overview should link to the detailed signal views rather than duplicating
full logs, traces, metrics, or monitoring.

### Degradation View

A service-scoped Degradation view can summarize recent findings by time
window. Each finding should explain:

- affected resource/service
- affected route, dependency, or runtime scope when known
- severity or confidence
- time window
- supporting evidence links
- recommended next view, such as Traces, Logs, Monitoring, Health, or
  Resource details

For MVP this can be query-time analysis over retained local signals. It does
not need a background alerting engine.

### Replica Breakdown

When a service has multiple runtime scopes, Resource Manager should show:

- request share per replica
- error share per replica
- latency comparison per replica when trace/metric dimensions allow it
- CPU, memory, restart count, uptime, and placement from resource monitoring
- whether one replica is missing telemetry or monitoring

The default view remains "All instances." Per-replica filters should be
available only when stable scope dimensions exist.

### Logs

Log views should keep improving as source-oriented inspection tools:

- filter by level
- filter by resource/source
- filter by structured fields such as route, status code, exception type,
  trace ID, span ID, category, and runtime scope
- text search
- time-window filtering

Degradation findings should deep-link into filtered logs when supporting log
evidence exists.

### Traces

Trace views should remain the primary cross-service request-flow tool. Error
spans already provide a useful path into degraded requests. Service-level
degradation should use traces to identify:

- failing spans
- slow dependency calls
- routes with elevated failure or latency
- resource and service relationships involved in a request

### Metrics

Telemetry Metrics should provide load and application/runtime measurements:

- raw retained points for inspection
- rate and window aggregations for service load
- route/status/replica breakdowns when dimensions exist

Resource Monitoring remains separate under Management because it describes
provider-observed process/container/runtime pressure.

### Monitoring

Monitoring should show capacity and runtime pressure:

- process/container CPU and memory
- restart count
- uptime
- network and block I/O where providers expose them
- provider observation status
- runtime host or placement context

For replicated container apps, Monitoring should summarize the service first
and then break down per replica/container.

## Initial Implementation Plan

1. Define a service-level query/view model that can combine recent traces,
   metrics, logs, health, and resource monitoring summaries for one resource.
2. Add explicit rate/window aggregation for telemetry metric panels, starting
   with request rate over a small set of fixed windows such as 1 minute,
   5 minutes, and 1 hour.
3. Add structured log filters for level, trace ID, span ID, route, status
   code, exception type, source, and time window where structured fields are
   available.
4. Add a service-scoped Degradation view that lists recent error spans,
   exceptions, failing routes, and health/resource-monitoring warnings with
   links to evidence.
5. Add replica-aware breakdowns for container apps when telemetry and
   monitoring carry stable runtime-scope dimensions.
6. Add provider/host placement labels to resource-pressure summaries so CPU
   and memory are interpreted in context.
7. Add a redacted public report generator for a selected service/resource.

## Relationship To Existing Proposals

- [Logging infrastructure](logging-infrastructure.md) owns log sources,
  structured log metadata, traces, telemetry metric ingestion, signal taxonomy,
  and correlation fields.
- [Resource monitoring](resource-monitoring.md) owns provider-observed
  process/container/runtime monitoring snapshots and Monitoring tabs.
- [Container applications](../containers/container-applications.md) owns
  container app replicas, runtime-managed child resources, app-scoped
  telemetry scopes, and provider-owned container app Monitoring dashboards.
- [Provider-created and runtime-managed resources](provider-created-and-runtime-managed-resources.md)
  owns runtime resource projection, ownership, visibility, cleanup, and future
  placement/materialization diagnostics.
- Future Health/status-page work can reuse service degradation findings, but
  broad uptime, incident annotation, subscriptions, and public status pages
  remain separate post-MVP work.

This proposal should not introduce a competing observability stack. When a
standard backend such as Prometheus, OpenTelemetry Collector, a trace store, or
a log store is a better source of truth, CloudShell should use domain-shaped
manager/query contracts to project the relevant signal back into the resource
experience.

## Remaining Tasks

- Decide whether the service-level query model belongs behind a new manager or
  is composed in the Resource Manager UI from existing managers for MVP.
- Define the minimum stable structured log fields needed for exception and
  route filtering.
- Define the telemetry scope dimensions that connect logs, spans, metrics, and
  resource monitoring to replicas.
- Decide how much query-time aggregation is acceptable before a dedicated
  telemetry aggregation store is needed.
- Define report redaction rules and access requirements.
- Decide whether degradation findings should later become retained records,
  resource events, health summaries, or stay query-time only.

## Open Questions

- Which fixed time windows are enough for the first load view?
- Should request-rate data prefer metric points, spans, or either source when
  both are available?
- How should missing telemetry from one replica be distinguished from zero
  traffic?
- What is the minimum provider placement vocabulary for local process, local
  Docker, remote Docker, and future orchestrators?
- Should public reports include short log excerpts, or only counts and links?
