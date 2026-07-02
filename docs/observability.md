# Observability

CloudShell treats observability as a set of related but separate resource
signals. Logs, resource activity, traces, metrics, monitoring, usage, and
health have different ownership and query shapes. They should not be collapsed
into one generic text-log model.

## Signal Types

| Signal | Owner | Purpose |
| --- | --- | --- |
| Logs | Provider or runtime integration | Source-addressed operational streams such as process output, container logs, provider logs, or file-backed logs. |
| Resource events | Control Plane / Resource Manager | Actor-attributed platform activity such as actions, lifecycle milestones, deployment updates, and provider procedure milestones. |
| Traces | Application/runtime telemetry ingestion | Correlated spans used for request and workflow investigation. |
| Telemetry metrics | Application/runtime telemetry ingestion | Time-series application or runtime measurements retained for telemetry views. |
| Resource monitoring | Provider/resource manager integration | Current provider-observed resource metrics such as CPU, memory, network, process, and runtime materialization state. |
| Usage | Control Plane persisted samples | Historical provider-observed usage values selected for reporting and trend analysis. |
| Health and liveness | Resource declarations plus Control Plane evaluation | Probe declarations and observed results used for resource status, liveness, and recovery decisions. |

Application/runtime telemetry belongs under Telemetry. Provider-observed
resource monitoring belongs under Management > Monitoring. Platform activity
belongs under Activity. This taxonomy keeps user-facing views aligned with
ownership boundaries.

## Logs

Logs are discovered through source metadata and read by source ID. A log source
can come from:

- a resource `ResourceLogSource` declaration
- an `ILogSourceContributor`
- an `ILogProvider` that contributes provider-owned or runtime-discovered
  sources

The Control Plane merges source declarations and contributed sources through
the log-source catalog. Consumers use `ILogManager`; providers implement
`ILogProvider` and materialize `ILogSourceSession` values for reads or streams.

`ResourceLogSource` is resource-model discovery metadata. `LogSource` is the
Control Plane projection used for listing, authorization, reading, streaming,
parsing, and rendering.

Current Control Plane routes:

```text
GET /api/control-plane/v1/log-sources
GET /api/control-plane/v1/log-sources/{logSourceId}
GET /api/control-plane/v1/log-sources/{logSourceId}/entries
GET /api/control-plane/v1/log-sources/{logSourceId}/stream
```

Source-addressed reads are bounded snapshots. Streaming is available only when
the source advertises streaming capability.

## Resource Events And Activity

Resource events are platform-owned activity records. They are not provider log
lines. They record facts such as requested actions, lifecycle milestones,
deployment updates, recovery decisions, and provider procedure milestones.

Consumers use `IResourceEventManager`; Control Plane services append through
the resource event store/sink. Resource Manager presents this stream as
Activity.

Current Control Plane route:

```text
GET /api/control-plane/v1/resource-events
```

The route supports filters for resource id, event type, triggering actor,
trace id, span id, time range, and maximum record count.

## Traces And Telemetry Metrics

Traces and telemetry metrics are application/runtime signals retained for
investigation. They are resource-scoped but not embedded in `Resource`.

Consumers use `ITraceManager` and `IMetricManager`. Runtime integrations can
ingest spans and metric points through the Control Plane ingestion routes.
Those ingestion routes are excluded from the public OpenAPI description and
allow anonymous ingestion for local runtime telemetry paths; deployment
configurations must still avoid exposing them as a general public endpoint.

Current Control Plane routes:

```text
GET /api/control-plane/v1/traces
POST /api/control-plane/v1/traces/ingest
GET /api/control-plane/v1/metrics
POST /api/control-plane/v1/metrics/ingest
```

Resource observability metadata can advertise telemetry sources and selectable
scopes such as replicas, workers, partitions, or runtime containers. Views can
use those scopes for trace and metric filtering.

## Monitoring And Usage

Monitoring is provider-observed current resource state. Usage is retained
historical usage data selected from provider observations. See
[Resource Monitoring and Usage](monitoring-and-usage.md) for the canonical
model, persistence boundary, and API routes.

## Health And Liveness

Health and liveness are declared through `health.checks` in
`ResourceDefinition` values. `ResourceHealthCheck` describes the probe type and
source. The Control Plane evaluates probes, stores observations, and derives
resource health/liveness state.

Current Control Plane routes include:

```text
GET /api/control-plane/v1/resource-health
GET /api/control-plane/v1/resources/{resourceId}/health
GET /api/control-plane/v1/resources/{resourceId}/health/snapshots
```

HTTP is the built-in probe source today. Providers can add non-HTTP evaluators
for process, container, runtime, or provider-native signals without making
every health check an HTTP endpoint.

## Permissions

Observability reads are controlled separately from general resource reads.
The grouped permission is `observability.read`, with narrower permissions for
logs, traces, and metrics:

- `observability.logs.read`
- `observability.traces.read`
- `observability.metrics.read`

See [Authentication and authorization](authentication-and-authorization.md)
and [Resource identity and permissions](resource-identity-and-permissions.md).

## Boundaries

- Do not expose secrets in logs, events, traces, metrics, monitoring, usage,
  or health diagnostics.
- Do not treat provider logs as platform activity. Use resource events for
  actor-attributed platform facts.
- Do not embed logs, events, traces, or metric history inside `Resource`.
  Query them through their managers.
- Do not treat provider-observed monitoring as application telemetry. Use
  Monitoring for resource/current provider observations and Telemetry for
  runtime/application investigation.
- Do not require every provider log entry to become structured immediately.
  Source metadata and parser/format metadata can evolve as provider needs are
  proven.
