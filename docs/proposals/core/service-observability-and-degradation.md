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

## Design Principles

CloudShell should build on established telemetry technologies and interfaces
instead of inventing private formats for common operational signals. The
default integration path should use OpenTelemetry concepts for traces,
metrics, service names, resource attributes, trace/span correlation, and
telemetry export where they fit. Structured logs should use familiar level,
category, event, trace ID, span ID, exception, route, status, and attributes
fields where a source can provide them.

CloudShell's job is to project those signals into Resource Manager through
resource-centered abstractions. Extension authors should be able to contribute
or consume logs, traces, telemetry metrics, monitoring snapshots, health, and
degradation summaries through CloudShell manager/query contracts without
binding their UI directly to one storage backend, generated HTTP client, or
provider implementation detail.

## Goals

- Provide a service-first overview that answers "what is degraded?" before
  asking users to choose logs, traces, metrics, or monitoring manually.
- Reuse established observability concepts such as OpenTelemetry traces,
  metrics, service names, resource attributes, and structured log fields.
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
- Provide abstractions that Resource Manager extensions can use to surface
  telemetry and monitoring signals consistently.

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

### Health Scope

A health scope is the status boundary described by a health signal or computed
degradation summary. It can be:

- the whole service observation target
- a selected resource set
- one dependency or dependency group
- one exposed service, route, or endpoint set
- one runtime scope, such as a replica or backing container

Health scopes let CloudShell distinguish "the resource is unavailable" from
"one dependency, route, replica, or selected resource set is degraded."
Scopes are a concept for naming and visualizing health boundaries; aggregate
health checks can still exist without explicit scope metadata. Scopes can come
from provider metadata, structured health-check payloads, or Control Plane
composition over known resources and relationships.

A scope names the aggregation target, not the aggregation algorithm. For a
Control Plane-computed resource-set scope, the implementation may combine
resource health checks, liveness signals, readiness signals, provider-owned
status, monitoring snapshots, and other resource factors into a scoped health
or degradation result.

Resource health checks remain resource-owned signals first. Resource Manager
can show them on each individual resource whether the backing application
serves the endpoint itself, a provider serves a native signal, or a container
app provider aggregates replica observations into the container app's resource
health check result. Health scopes are then built from those observed
resource-level checks and related signals.

In the future, the Control Plane can expose CloudShell-provided health check
endpoints for health scopes. For example, it could expose a health endpoint
for an application topology, a frontend plus its declared backend and SQL
dependencies, or a container app and all of its replicas. Those endpoints
would be derived from resource health snapshots, monitoring snapshots,
telemetry, and provider-owned status, rather than requiring each application
to implement the same aggregate endpoint itself.

The global Health surface can later manage these Control Plane-computed
scopes. A user could create a health scope, add resources or specific resource
health checks to it, select which liveness signals, readiness signals,
monitoring data, or provider factors contribute, and then use the generated
Control Plane endpoint as the health endpoint for that scope.

Container apps need a provider-owned scoped health model because the stable
resource can represent one or many running replicas. The resource-level health
check declaration should stay on the container app definition as the template
for runtime replica checks. When multiple replicas or backing containers
exist, the provider should project the declared health and liveness checks
onto the runtime scopes that can actually be probed. CloudShell can then
aggregate the replica observations into the container app's resource health or
liveness result and expose the per-replica breakdown for Health, Monitoring,
and Degradation views. When exactly one replica is running, the aggregate
result and the runtime-scope result can be the same observation.

The first container app slice keeps the declaration on the container app
definition and projects replicated HTTP checks onto hidden runtime replica
resources. Those checks may remain unresolved until the provider model can
expose replica-specific probe addresses or provider-native replica health.
The container app aggregate health result should be introduced separately as a
Control Plane aggregation over the observed replica checks.

This should be expressed in the shared application resource toolkit rather
than as a one-off container app exception. Application-like resource types can
choose different health ownership policies: executable applications,
project-backed applications, and single-process service resources may expose
resource-owned health and liveness directly, while replicated container apps
project the declared checks onto runtime replicas and aggregate the replica
state back to the stable application resource.

The aggregation policy is provider-owned. One failed replica might degrade the
container app while enough healthy replicas remain to serve traffic, while all
replicas failing or the runtime disappearing should move the resource toward a
stopped or unavailable lifecycle state when the provider can make that
distinction. Recovery should consume the aggregate liveness outcome for the
stable container app resource unless a later provider model supports
replica-specific recovery operations.

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

Health can be one of the inputs to degradation, and it may be aggregate or
scoped. A resource-level HTTP health endpoint might front many services,
dependencies, routes, runtime instances, or a selected set of related
CloudShell resources. For example, a frontend application can expose a JSON
health response that includes its own status, backend API dependency status,
SQL Server connectivity, and per-replica status. When a health signal is
aggregated, the degradation view should preserve the health-scope breakdown
if the payload, provider, or Control Plane can expose it: which service,
dependency, route, instance, replica, partition, backing container, or
referenced resource contributed to the degraded result. The stable resource
remains the entry point, while runtime scopes, service scopes, and
related-resource scopes provide drill-down.

This allows Health to say "the resource is partially degraded" without
claiming the whole resource is dead and without requiring every sub-service to
be modeled as a separate resource. It also gives future Health dashboards
enough structure to chart aggregate status and per-scope status over time.
Recovery should still consume explicit liveness signals rather than broad
aggregate Health status unless a provider or operator maps a specific
aggregate result as the recovery signal.

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

The current implementation should keep resource health check declarations as
metadata for resource-provided health signals. Health check results can now
carry scoped observations returned by evaluators, which lets a provider-owned
aggregate check report per-replica, dependency, route, or runtime-scope detail
without adding scope metadata to every resource health check declaration.
Health scopes remain a future Control Plane-managed aggregation concept built
from the observed state CloudShell collects by polling those signals,
liveness checks, readiness checks, provider status, and monitoring sources.
The next implementation slice should introduce a provider-owned runtime-scope
aggregation model for container apps and a separate Control Plane-owned health
scope definition and status model.

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
