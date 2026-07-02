# Replicated Container Health Sample

This sample declares a replicated ASP.NET Core container application with HTTP
health and liveness probes. The demo API also emits JSON console logs,
CloudShell trace spans, and request metrics so Resource Manager can show
replica-scoped runtime data from the stable parent resource.

Run the sample:

```bash
dotnet run --project samples/ReplicatedContainerHealth/CloudShell.ReplicatedContainerHealth.csproj -- --urls http://localhost:5011
```

The stable `application.container-app:api` resource owns the workload
declaration, service endpoint, and requested replica count. The hidden runtime
replica resources own materialized probe targets while the app is active.

The sample keeps the app stopped by default so Resource Manager projection,
health declarations, and UI tabs can be tested without starting Docker
containers. Starting `application.container-app:api` creates runtime replica
containers plus the `cloudshell-replicated-health-api-ingress` Traefik
container. The stable endpoint points at that ingress container; replica probe
ports are diagnostics and health targets. Set `ReplicatedContainerHealth:ApiPort`
when another local app port is needed.

## Resource Model Coverage

The sample declares only Resource Definitions-backed resources:

- `docker.host:sample`: Docker host used by the sample runtime.
- `application.container-app:api`: replicated container app with three
  requested replicas, a typed startup dependency on `docker.host:sample`, local
  `http` endpoint request, cookie session affinity for routing, `/health`
  health check, and `/alive` liveness check.

The old `application:api` and old Docker provider records are no longer
declared. Starting the container app uses the provider-owned local Docker
container-app runtime adapter. It builds the API image, starts replica
containers, starts the sample-owned Traefik ingress container, projects hidden
runtime replicas, and removes those containers on stop. Image and replica
updates are applied through ResourceDefinition changes and then delegated
through the container-app runtime operation seam.

The app declares cookie session affinity with the `CloudShellReplica` cookie.
The current sample projects that setting into the orchestrator service routing
binding so the Resource Manager UI can edit and inspect it. During routing
reconciliation, the sample Traefik bridge writes sticky-cookie configuration
for that binding so repeated HTTP requests that carry the affinity cookie can
stay pinned to the same replica. This is ordinary endpoint-wide sticky routing;
it is useful for SignalR and WebSocket setup flows, but it also affects normal
HTTP requests from the same client.

Replica telemetry is posted back to the Control Plane from inside the
containers. For local `localhost` sample URLs, the sample maps the runtime
Control Plane endpoint to `host.docker.internal`. Override
`Observability:RuntimeEndpoint` when your Docker host uses a different address
for reaching the host machine.

## Runtime Seams

These implementation seams remain temporary while the durable orchestrator is
being designed:

- `LocalDockerContainerApplicationRuntimeBridge` is the
  provider-owned local Docker/Traefik runtime bridge. It is reusable local
  development plumbing, not the final container-app orchestrator.
- `LocalDockerContainerApplicationRuntimeResourceProvider` projects hidden
  runtime-managed replica resources through the existing flat
  `IResourceProvider` adapter. The future provider contract should distinguish
  top-level resources from optional runtime/sub-resource projections.
- Runtime replica log and monitoring providers are now supplied by the
  container-app provider. The sample only projects hidden runtime replica
  resources with the container metadata those providers consume.
- The sample-local image update endpoint exists to exercise
  `ResourceDefinition` overlay apply plus operation delegation. It should be
  replaced by the eventual Control Plane API for applying resource graph
  changes.

Current known gaps:

- Runtime projection still uses running/stopped/unknown states. A durable
  runtime should model container-app startup separately so newly started
  replicas can show a starting state while health converges.
- Generated telemetry tabs for hidden runtime replica resources still need
  runtime-resource-specific details handling.
