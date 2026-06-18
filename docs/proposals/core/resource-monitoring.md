# Resource Monitoring Proposal

## Status

In progress.

Resource monitoring is the provider-observed resource health and usage surface
for CloudShell resources. It belongs under the Resource Manager Management
group and is separate from application Telemetry metrics.

## Problem

Operators need to answer resource-management questions while staying on the
resource detail page:

- Is this container or process currently using CPU?
- How much memory is it consuming?
- Is the provider able to observe the resource right now?
- Which provider produced the observation?

CloudShell already distinguishes application telemetry from resource events.
It also needs a separate place for resource metrics so process/container usage
does not get mixed into application-level request counts, durations, traces,
or application health checks.

## Goals

- Keep provider-observed resource metrics separate from application telemetry
  metrics.
- Show resource monitoring in context under the resource Management group.
- Let providers opt in resource by resource.
- Support split hosting through domain-shaped managers and Control Plane API
  routes.
- Start with current snapshots for MVP rather than durable metric history.
- Keep secrets and provider credentials out of monitoring snapshots.

## Non-Goals

- Do not build a durable time-series database for MVP.
- Do not model application-level health checks as resource-level health checks.
- Do not require all providers to expose the same metric set immediately.
- Do not replace application Telemetry Metrics or shared Telemetry views.
- Do not standardize provider-specific charts before concrete providers prove
  what they need.

## Model

Resource monitoring is provider-owned observed state. A provider answers
whether it can monitor a projected `Resource` and can return a current
`ResourceMonitoringSnapshot`.

The first model shape is:

- `IResourceMonitoringProvider`: provider opt-in and snapshot query contract.
- `IResourceMonitoringManager`: shell/client-facing Control Plane manager.
- `ResourceMonitoringSnapshot`: one provider observation for one resource at
  one timestamp.
- `ResourceMetricSample`: a named resource metric value such as CPU percent or
  memory bytes.

This is intentionally different from telemetry metric ingestion. Telemetry
metrics represent application/runtime instrumentation such as request count,
request duration, queue depth, or service-specific counters. Resource metrics
represent provider-observed process/container/runtime usage such as CPU,
memory, restart count, or provider runtime status.

The generic snapshot contract is intentionally per resource. Multi-instance
resources such as `application.container-app` can still use resource metric
samples with replica/container attributes, but their primary Resource Manager
Monitoring experience should be provider-owned. A container app Monitoring tab
needs to summarize app-level usage and show each projected runtime replica or
container separately without forcing users into the global runtime-managed
inventory or treating implementation containers as the stable app surface.

## Resource Manager UX

Resource Manager uses the standard predefined view ID
`management:monitoring`. The generated Monitoring tab appears under the
Management group only when a provider reports support for the selected
resource.

The generated tab should show:

- provider name
- snapshot timestamp
- provider status/message
- compact resource metric cards
- refresh action

Providers can later replace the generated tab with a provider-owned
Monitoring tab when they need charts, history, runtime-specific detail, or
advanced diagnostics. Container applications are a first-class example: their
Monitoring tab should show aggregate app resource usage plus a per-replica
breakdown for CPU, memory, process count, network I/O, block I/O, restart
count, uptime, and provider health/materialization details when the runtime
provider can observe them.

## Current Implementation

The first implementation slice adds:

- provider-backed resource monitoring contracts
- Control Plane manager, API, and remote-client projection
- generated Resource Manager Monitoring tab
- Docker container CPU, memory, network I/O, block I/O, process count,
  restart count, and uptime snapshots
- application process CPU, CPU time, memory, thread count, process count, and
  uptime snapshots for executable and ASP.NET Core project resources

Docker-backed container resources report current CPU usage, memory usage,
memory limit, memory usage percentage, network bytes received/sent, block
bytes read/written, process count, restart count, and container uptime from
Docker stats and inspection data when the container is running. Stopped
containers can still expose the Monitoring tab, but the snapshot reports that
live Docker metrics are unavailable until the container is running.

Executable application and ASP.NET Core project resources report process CPU
usage, total CPU time, working-set memory, private memory, thread count,
process count, and uptime when the local application process is running.
Container-backed application resources remain excluded from the generated
process snapshot path because container app monitoring needs an app-level
summary and per-replica/container breakdown.

## Remaining Work

- Add provider-owned Monitoring tabs when generated metric cards are too
  limited.
- Add a container app Monitoring tab that summarizes app-level resource usage
  and shows per-replica/container metrics for replicated applications.
- Add resource-metric history and charting after concrete providers prove the
  retention needs.
- Decide whether resource monitoring snapshots should emit resource events or
  diagnostics when providers cannot observe a resource.
- Decide whether CloudShell needs a separate resource-level health-check model
  beyond current application-level health checks.
- Add monitoring providers for other runtime providers as they land.

## Future Questions

- Should live Monitoring views subscribe through the Control Plane API rather
  than poll current snapshots, and should the first ASP.NET Core transport use
  SignalR/WebSockets or a polling fallback?

## Relationship To Observability

See [Logging infrastructure](logging-infrastructure.md) for the broader
operational signal taxonomy. The durable split is:

- Resource Events: management history under Management.
- Resource Metrics: provider-observed resource monitoring under Management.
- Telemetry Logs/Traces/Metrics: application/runtime investigation under
  Telemetry.
