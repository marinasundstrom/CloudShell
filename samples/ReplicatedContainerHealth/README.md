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
ports for each runtime replica.

## Resource Graph POC coverage

The sample also declares side-by-side graph-backed resources through the
Resource Definitions bridge:

- `docker:graph-sample`: graph-backed Docker host.
- `application.container-app:graph-api`: graph-backed replicated container app
  projection with a replica count of `3` and a typed startup dependency on the
  graph Docker host.

Those resources prove projection, replica-count attributes, and dependency
shape while the existing application/Docker provider path remains responsible
for replica materialization, runtime health aggregation, logs, traces, and
metrics.

When running the replica health path against Docker, start the `api` resource.
The app start builds the project container image into the local Docker image
store before creating the replicas.

After the app starts, browse `http://localhost:5092/work` a few times to
generate demo logs, spans, and metrics from the replicas. The replica scope
attributes are provided through the container app runtime environment and are
preserved by the sample service defaults when telemetry is ingested into the
Control Plane.
