# Resource Monitoring Proposal

## Status

In progress.

The implemented monitoring and usage behavior is now specified in
[Resource Monitoring and Usage](../../monitoring-and-usage.md). This proposal
tracks only the remaining incremental work needed to improve provider coverage,
diagnostics, live update transport, and history behavior.

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

- `ResourceCapabilityIds.Monitoring`: stable resource graph signal that the
  resource supports provider-observed resource monitoring.
- `IResourceMonitoringProvider`: provider opt-in and snapshot query contract.
- `IResourceMonitoringManager`: shell/client-facing Control Plane manager.
- `ResourceMonitoringSnapshot`: one provider observation for one resource at
  one timestamp.
- `ResourceMetricSample`: a named resource metric value such as CPU percent or
  memory bytes.

The capability advertises the role on the projected resource. A resource may
use the generated snapshot view or a provider-owned Monitoring tab when it
needs a richer view. The provider contract remains the runtime authority for
split-hosted or stale resource graphs because the active Control Plane must
still decide whether it can observe the selected resource and return a current
snapshot.

This is intentionally different from telemetry metric ingestion. Telemetry
metrics represent application/runtime instrumentation such as request count,
request duration, queue depth, or service-specific counters. Resource metrics
represent provider-observed process/container/runtime usage such as CPU,
memory, restart count, or provider runtime status.

The generic snapshot contract is intentionally per resource. Multi-instance
resources such as `application.container-app` can still use resource metric
samples with replica/container attributes, but their primary Resource Manager
Monitoring experience should be provider-owned. A container app Monitoring tab
needs to summarize app-level usage and show each materialized runtime replica or
container separately without forcing users into the global runtime-managed
inventory or treating implementation containers as the stable app surface.

## Resource Manager UX

Resource Manager uses the standard predefined view ID
`management:monitoring`. The generated Monitoring tab appears under the
Management group only when the resource advertises the monitoring capability
and a provider reports support for the selected resource.

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

## Remaining Work

- Add provider-owned Monitoring tabs for resource types where generated metric
  cards are too limited.
- Enrich container app Monitoring with provider-observed container IDs,
  placement, health, restart count, uptime, and materialization diagnostics as
  providers report them consistently.
- Decide which metrics should be promoted from snapshot-only monitoring into
  retained usage history and charts.
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
