# Resource Monitoring and Usage

CloudShell separates provider-observed resource monitoring from application
telemetry and retained usage history.

Resource monitoring answers what a provider can observe about a resource right
now: CPU, memory, process count, container network counters, storage bytes, or
another provider-owned runtime metric. Usage records selected monitoring
samples over time so Resource Manager can show environment and resource trends.
Application telemetry remains the place for request counts, spans, logs,
application metrics, and service-level investigation.

For the remaining design work, see the
[Resource Monitoring proposal](proposals/core/resource-monitoring.md).

## Resource Monitoring

Resource monitoring is provider-owned observed state. Providers opt in with
`IResourceMonitoringProvider`, the Control Plane exposes the resource-scoped
`IResourceMonitoringManager`, and resources advertise generated monitoring
support with the `monitoring` resource capability.

The current snapshot model uses:

- `ResourceCapabilityIds.Monitoring` to signal that a resource supports
  provider-observed monitoring.
- `IResourceMonitoringProvider` for provider availability and snapshot
  queries.
- `IResourceMonitoringManager` for shell and remote-client access.
- `ResourceMonitoringSnapshot` for one provider observation at one time.
- `ResourceMetricSample` for named metric values such as CPU percent, memory
  bytes, network bytes, process count, restart count, or storage bytes.

Resource Manager exposes generated monitoring under
`Management > Monitoring` with the predefined view ID
`management:monitoring`. A provider can replace that generated view with a
provider-owned Monitoring tab when the resource needs charts, replica
breakdowns, placement details, or runtime-specific diagnostics.

## Current Providers

Docker-backed container resources report current CPU usage, memory usage,
memory limit, memory utilization, network bytes, block bytes, process count,
restart count, and uptime when Docker can observe the running container.
Stopped containers can still expose Monitoring, but the snapshot reports that
live Docker metrics are unavailable until the container is running.

Executable applications, ASP.NET Core project resources, JavaScript
applications, Java applications, Configuration Store resources, and Secrets
Vault resources can report local process CPU, total CPU time, working-set
memory, private memory, thread count, process count, and uptime when their
provider-owned process is running.

Container apps use a provider-owned Monitoring tab. The tab summarizes
single-instance app metrics or replicated app metrics by materialized runtime
replica/container. Missing CPU, memory, network, process, or provider counters
are displayed as not collected rather than mixed with zero values.

Storage and volume providers can report used bytes, configured max size,
remaining bytes, utilization, and max-size-reached observations. A volume max
size is a monitoring boundary and warning signal unless the backing storage
provider can prove hard quota enforcement.

## Usage

Usage is retained sample history, not live telemetry. Resource monitoring can
automatically record provider-observed CPU, memory, network, process, storage,
and custom metrics as usage samples. Resource Manager exposes:

- an environment-wide Usage workspace
- resource-scoped Usage tabs
- aggregate statistics
- short-horizon trend projections
- drill-down tables for detailed statistics and recent metric samples

Usage samples are retained in memory by default. Hosts can opt into
database-backed sample persistence with per-resource retention settings; see
[Persistence](persistence.md#usage). Access is gated by `usage.read`; see
[Authentication and authorization](authentication-and-authorization.md#usage-permissions).

Usage records must not contain secrets. Metric metadata can include non-secret
facts such as provider name, metric display name, unit, scope, and resource ID
so dashboards can summarize usage without depending on one provider shape.

Usage views should be summary-first. The default overview should explain usage
through aggregate cards and trend charts, while detailed metric inventories and
recent sample tables remain available as secondary drill-down content. Future
provider and shell extension work should allow hosts to contribute customized
usage summaries, charts, and metric groupings for their users without replacing
the underlying usage sample/statistics APIs.

## API

Resource monitoring uses resource-scoped Control Plane API routes:

```http
GET /api/control-plane/v1/resources/{resourceId}/monitoring/availability
GET /api/control-plane/v1/resources/{resourceId}/monitoring
```

The first route lets Resource Manager decide whether to show the generated
Monitoring tab. The second route returns the current provider snapshot.

Usage exposes separate Control Plane API/client methods for retained samples,
aggregate statistics, and trend summaries. Usage APIs are permission-gated and
resource-scoped queries still require resource read access for the resources
whose samples are returned.

## Boundaries

Use this split when adding new operational signals:

- Resource Events: management history under Management.
- Resource Monitoring: provider-observed current resource metrics under
  Management.
- Usage: retained resource usage samples and trends.
- Telemetry Logs, Traces, and Metrics: application/runtime investigation under
  Telemetry.

Live subscriptions for monitoring or telemetry remain a later API design
question. The current contract is snapshot and query based.
