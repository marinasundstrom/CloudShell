# Replicated Container Health Sample

This sample declares a replicated ASP.NET Core container application with HTTP
health and liveness probes. The demo API also emits JSON console logs,
CloudShell trace spans, and request metrics so the container app's Logs,
Traces, Metrics, and Monitoring views can show the replica scope from the
stable parent resource.

The stable `application:api` resource owns the workload declaration, service
endpoint, and replica count. The hidden runtime replica resources own the
materialized probe targets while the app is active. CloudShell polls those
runtime liveness observations and health-check signals, then materializes an
aggregate health assessment on the stable container app resource.

The sample uses the Docker host's local image store by default. The ASP.NET
Core project is published as:

```text
cloudshell-application-api:20260622.2
```

The tag is intentionally explicit so repeated replica-health runs use a
predictable image reference. Configure a registry on the Docker host and pass
the same registry to `AsContainer(...)` only when the image must be pushed to a
separate target registry.

Replica telemetry is posted back to the Control Plane from inside the
containers. For local `localhost` sample URLs, the sample maps the runtime
Control Plane endpoint to `host.docker.internal`. Override
`Observability:RuntimeEndpoint` when your Docker host uses a different address
for reaching the host machine.

The sample keeps the app stopped by default so the resource model, Health tab,
and Control Plane health endpoints can be tested without requiring Docker to
start the containers. Start the Docker host and then the `api` resource to
publish the app ingress on `http://localhost:5092` and separate probe-only
ports for each runtime replica. Set `ReplicatedContainerHealth:ApiPort` when
the sample should publish the app ingress on a different local port.

## Resource Graph POC coverage

The sample also declares side-by-side graph-backed resources through the
Resource Definitions bridge:

- `docker:graph-sample`: graph-backed Docker host.
- `application.container-app:graph-api`: graph-backed replicated container app
  projection with a replica count of `3` and a typed startup dependency on the
  graph Docker host. It also declares the local `http` endpoint request plus
  health and liveness checks for `/health` and `/alive`.

Those resources prove projection, replica-count attributes, endpoint mapping,
health/liveness declarations, and dependency shape while the existing
application/Docker provider path remains responsible for replica
materialization, runtime health aggregation, logs, traces, and metrics.
The sample now wires the graph container-app lifecycle operation to a
sample-local bridge contract, so starting
`application.container-app:graph-api` delegates to the current bridge
implementation. The current bridge still targets the existing `application:api`
runtime app while the graph-only container runtime is being refined, but the
graph handler itself is no longer coupled directly to old application-provider
services. The graph container app projects its Resource Manager state through
the container-app runtime handler/status seam and the Resource Manager bridge
so graph stop and restart can be evaluated and delegated as well. The Docker
smoke also verifies that graph stop removes the revision-scoped runtime
containers created by graph start. The sample exposes a sample-local graph
image update endpoint used by smoke coverage to apply a `ResourceDefinition`
overlay and then delegate the graph `container.image.update` operation into the
existing runtime app configuration.

Set `ReplicatedContainerHealth:GraphOnly` to `true` to declare only the
graph-backed Docker host and container-app resources. In this mode the sample
does not register the old application or Docker provider path and does not
declare `application:api` or `docker:sample`. This is a switch-readiness gate:
it proves the graph resource shape without the old provider records, and it
wires the graph container app to a sample-local Docker bridge that publishes
the API container image, starts/removes the graph-owned replica containers, and
restarts those replicas when graph image or replica attributes are applied.
The same bridge removes a bounded range of graph-owned replica containers
during cleanup so scale-down does not leave stale higher-ordinal replicas
running, projects basic running/stopped state by inspecting the graph-owned
replica containers, and contributes provider-projected replica container log
sources for the graph container app. A graph-only runtime resource provider
also projects hidden runtime-managed replica resources from the accepted graph
state and sample runtime convention so the existing Control Plane health
aggregation path can evaluate runtime-scope health/liveness without writing
runtime observations back into the graph. The graph-only Docker bridge also
assigns each replica container the projected runtime replica resource ID,
OpenTelemetry service name, and telemetry scope attributes so runtime
observability can be correlated with the hidden replica resource projection.
Docker smoke coverage now verifies graph-only image update, replica update,
stale replica removal, direct graph-declared HTTP health/liveness refresh,
runtime-scope health aggregation, log source discovery, Docker log reading,
the running replica container's projected runtime observability environment,
and live trace/metric ingestion under the projected runtime replica resource
ID without the old provider records. The hidden runtime replica resource
projection now also advertises Resource Manager logs, traces, metrics, service
name, and runtime telemetry scope metadata so the projected resource and the
emitted telemetry line up. Generated Resource Manager telemetry tabs for the
hidden runtime replica resources are still a parity gap: a graph-only Docker
smoke investigation confirmed that opening a hidden replica's generated Traces
details route can time out even though telemetry is ingested and queryable
through the Control Plane API. Keep this as a Resource Manager
runtime-resource details seam to fix later rather than moving operational
behavior into the graph model.

### Temporary switch seams

These seams are intentional switch scaffolding and should be swept after the
sample runs successfully through the new providers:

- `ReplicatedContainerHealthGraphResourceManagerBridge` delegates side-by-side
  graph operations into the old `application:api` Resource Manager resource.
- `ReplicatedContainerHealthGraphOnlyContainerAppRuntimeBridge` is a
  sample-local Docker runtime bridge for graph-only mode. It should either
  move into the new provider/runtime structure or be replaced by the durable
  runtime implementation.
- `IReplicatedContainerHealthCommandRunner` keeps shell execution testable for
  the sample-local bridge. Its shape should not become shared infrastructure
  unless another provider proves the same command boundary is needed.
- Graph-only state projection uses bounded, cached Docker inspection so normal
  Resource Manager rendering does not block on Docker responsiveness.
- Graph-only lifecycle and update cleanup removes a bounded replica range so
  stale graph-owned containers are swept after scale-down; this is sample
  switch scaffolding, not a shared orchestration abstraction.
- `ReplicatedContainerHealthGraphOnlyRuntimeResourceProvider` projects hidden
  runtime-managed replica resources through the existing flat
  `IResourceProvider` adapter. This is intentionally not a new graph provider
  abstraction; the future provider contract should distinguish top-level
  resource enumeration from optional sub-resource/runtime projection after the
  switch succeeds. It also owns the temporary Resource Manager observability
  projection for those hidden runtime replicas. The generated details UI still
  needs a runtime-resource-specific treatment so telemetry tabs for hidden
  runtime replicas render from the operational telemetry path without blocking
  the generated details route.
- The graph-only Docker bridge sets runtime replica IDs and telemetry scope
  environment variables on the containers it creates. This is runtime
  integration wiring for the projected replica resources; it should move with
  the durable runtime implementation rather than becoming graph-model state.
- `ReplicatedContainerHealthGraphOnlyLogProvider` contributes graph-owned
  replica container log sources and reads Docker logs through the sample
  command runner. It intentionally does not bridge back into the old
  application-provider log parser; promotion to a shared provider runtime
  should wait until another port proves the same Docker-backed behavior is
  needed.
- The sample-local graph image update endpoint exists to exercise
  `ResourceDefinition` overlay apply plus graph operation delegation. It should
  be replaced by the eventual Resource Manager/control-plane API surface for
  applying graph changes.

When running the replica health path against Docker, start the `api` resource.
The app start builds the project container image into the local Docker image
store before creating the replicas.

After the app starts, browse `http://localhost:5092/work` a few times to
generate demo logs, spans, and metrics from the replicas. The replica scope
attributes are provided through the container app runtime environment and are
preserved by the sample service defaults when telemetry is ingested into the
Control Plane.
