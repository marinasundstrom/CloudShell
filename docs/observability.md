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
`ILogProvider` and materialize `ILogSourceSession` values for bounded history
reads, live streams, or both. A single-source consumer opens a session with one
source ID; there is no separate source-read API.

Consumers that need a common view over several sources open an `ILogSession`
through `ILogManager.OpenLogSessionAsync(...)`. The session is an
operation-scoped fan-in boundary owned by the Control Plane: it opens the
selected provider sessions once, merges bounded reads chronologically, fans in
live-capable sources through one bounded stream, and disposes every underlying
reader when the consumer disconnects. `LogSessionEntry` preserves the stable
`SourceId` beside each `LogEntry`; consumers must not infer source identity from
the display-oriented `LogEntry.Source` value.

Sessions are the public integration model for combined logs. Providers still
implement one source at a time and may share physical readers behind their
`ILogSourceSession` implementations. Extensions, remote clients, CLIs, and the
CloudShell UI consume the same `ILogSession` contract instead of creating
parallel provider subscriptions themselves.

The log manager does not collect, record, persist, rotate, or retain provider
logs. Those responsibilities remain with the source owner or its backing log
system. A source session may read buffered process output, page and tail a
file, follow a container runtime, or delegate query and streaming to an
external system such as Loki. This keeps the Control Plane on the authorization,
catalog, and operation-scoped coordination path rather than making it a log
data plane. Providers may share readers or move fan-in into their backing
system without changing the consumer contract.

`LogSourceCapabilities` describe what a source supports; they do not prescribe
how it is implemented. New source kinds and capabilities should extend source
metadata and provider sessions instead of adding provider-specific methods to
`ILogManager`. Query pushdown, reconnect cursors, and partial-source diagnostics
remain compatible future additions to the session model.

`ResourceLogSource` is resource-model discovery metadata. `LogSource` is the
Control Plane projection used for listing, authorization, reading, streaming,
parsing, and rendering.

Current Control Plane routes:

```text
GET /api/control-plane/v1/log-sources
GET /api/control-plane/v1/log-sources/{logSourceId}
GET /api/control-plane/v1/log-sessions/entries?sourceId={logSourceId}
GET /api/control-plane/v1/log-sessions/stream?sourceId={logSourceId}
```

Source-addressed reads are bounded snapshots. Streaming is available only when
the source advertises streaming capability. The session routes accept repeated
`sourceId` query parameters and return source-addressed entry envelopes. The
stream route uses NDJSON and remains open while any selected live-capable
source is producing entries. Non-streaming sources participate in the initial
history window but do not prevent the other selected sources from streaming.
Providers must not advertise `Stream` for an implementation that only replays
the current snapshot and completes. Polling a provider-owned bounded process
buffer is a valid live-follow implementation; it remains a provider concern and
must honor cancellation promptly.

### Built-in file sources

The built-in `FileLogProvider` opens explicitly declared UTF-8 files through
the same source-session contract. It reads a bounded byte window from the end
of the file, returns only complete lines, follows appended lines, and resets its
cursor when the file is truncated or replaced. Plain-text and JSON console
formats use the common `LogEntryParser`; the process writing the file remains
responsible for recording, flushing, rotation, and retention.

File access is disabled until the Control Plane deployment configures one or
more absolute allowed roots. A declared file must remain inside one of those
roots, and the provider rejects symbolic-link or reparse-point traversal below
the root. The path is revalidated for every read and follow poll.

```json
{
  "CloudShell": {
    "Logs": {
      "Files": {
        "AllowedRoots": ["/srv/cloudshell-apps/logs"],
        "PollInterval": "00:00:00.250",
        "MaxSnapshotBytes": 1048576,
        "MaxLineLength": 65536
      }
    }
  }
}
```

Programmatic resource declarations add a source without changing the manager
or UI API:

```csharp
resources
    .AddDotnetProject("api", projectPath)
    .WithFileLogSource(
        "application-file",
        "Application file",
        "/srv/cloudshell-apps/logs/api/application.log",
        ResourceLogSourceDefinitionValues.JsonConsole);
```

`WithFileLogSource` declares `File` storage plus `Read` and `Stream`
capabilities, and JSON console sources also advertise structured fields. The
provider will not open relative paths or paths outside host
policy. File patterns, rolling-file history across archived files, non-UTF-8
encodings, reconnect cursors, and shared physical tail readers remain separate
provider increments.

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
