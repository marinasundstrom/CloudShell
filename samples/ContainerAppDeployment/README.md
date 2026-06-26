# Container App Deployment Sample

This sample models a container app deployed next to a local registry resource.
It is intended to show the Control Plane deployment flow without requiring a
real build server.

The resource graph declares:

- `docker:sample`: the local Docker environment.
- `docker:container:sample-registry`: a local registry instance at
  `localhost:5023`.
- `application:sample-api`: a container app that depends on the registry for
  lifecycle ordering, references it for service discovery, and starts from the
  mock image tag `cloudshell/mock-api:20260608.1`.

> We use port `5023` instead of the common local registry default `5000`
> because macOS commonly reserves `5000` for host services such as AirPlay
> Receiver. Keep the registry port explicit when testing local deployment
> flows.

Override the registry port with configuration when `5023` is unavailable:

```bash
ContainerAppDeployment__RegistryPort=18023 \
  dotnet run --project samples/ContainerAppDeployment/CloudShell.ContainerAppDeployment.csproj -- --urls http://localhost:5007
```

The sample keeps the registry and app stopped by default. That makes the
revision flow safe to run even when the mock image has not actually been pushed.
When you do start the resources, Docker expects the referenced image tags to
exist in the configured registry address.

For local runs, use `create-registry.sh` to materialize the registry container
in Docker before pushing images to it. The declared Docker container resource
keeps the registry visible in CloudShell and preserves the dependency and
endpoint relationship, but the current sample does not create the registry
container from the resource declaration.

The app declaration keeps the lifecycle dependency and endpoint discovery
relationship explicit:

- `DependsOn(registry)` records that the app should start after the local
  registry is available.
- `WithReference(registry).WithServiceDiscovery()` projects registry endpoints
  into the workload's service discovery configuration.

The Docker host, registry resource, and container app all call
`Persist(overwrite: true)`. This keeps the sample focused on the handoff from
programmatic declarations into durable Control Plane/provider state. Resource
Manager should show these as persisted declarations rather than transient
startup declarations.

## Resource Graph POC coverage

The sample also declares side-by-side graph-backed resources through the
Resource Definitions bridge and provider-owned graph builders:

- `docker:graph-sample`: graph-backed Docker host with the configured registry
  address.
- `docker.container:graph-sample-registry`: graph-backed registry container
  projection.
- `application.container-app:graph-sample-api`: graph-backed container app
  projection with typed startup dependencies on the graph Docker host and
  registry container.

Those resources prove projection, registry attribute mapping, and dependency
shape while the existing application/Docker provider path remains responsible
for deployment requests, registry runtime behavior, image updates, and
container materialization.

The sample also includes an opt-in graph registry runtime materializer behind:

```bash
ContainerAppDeployment__EnableGraphDockerRuntime=true
```

When enabled, graph `start`, `restart`, and `stop` operations for
`docker.container:graph-sample-registry` create, recreate, and remove a local
Docker registry container named
`cloudshell-container-app-deployment-graph-registry`. It is disabled by default
so normal sample projection and smoke coverage do not depend on Docker CLI
availability or latency. The Docker command runner is sample-local and covered
by deterministic command-construction tests; it is not the durable Docker
provider runtime implementation. A provider-owned Docker runtime implementation
and full lifecycle smoke coverage are still POC follow-up work.

Run the sample:

```bash
dotnet run --project samples/ContainerAppDeployment/CloudShell.ContainerAppDeployment.csproj -- --urls http://localhost:5007
```

Simulate a new build/deploy:

```bash
samples/ContainerAppDeployment/deploy-mock-image.sh
```

Create the local registry on a matching alternate port:

```bash
CONTAINER_APP_DEPLOYMENT_REGISTRY_PORT=18023 \
  samples/ContainerAppDeployment/create-registry.sh
CONTAINER_APP_DEPLOYMENT_REGISTRY_PORT=18023 \
  samples/ContainerAppDeployment/deploy-mock-image.sh
```

Or pass an explicit app ID and tag:

```bash
samples/ContainerAppDeployment/deploy-mock-image.sh application:sample-api 20260608.2
```

The script posts to:

```text
POST /api/container-apps/v1/{containerAppId}/deployments
```

with a new image tag. The old `application:sample-api` path creates a container
app deployment, records the app-owned revision produced by that deployment, and
writes resource events for traceability. The graph-backed
`application.container-app:graph-sample-api` path uses the same API to apply
`container.image` and optional `container.replicas` changes into the Resource
Graph before executing a temporary graph image-update operation seam. In this
sample, that seam is wired to a sample-local adapter over the existing
`application:sample-api` runtime path so the graph update also changes the
current runtime app declaration. This is a POC bridge for accepted graph
changes; in the current API path, image upload and deployment creation remain
Control Plane workflows.
The graph-backed app also supports the existing:

```text
PUT /api/container-apps/v1/{containerAppId}/replicas
```

route, which applies `container.replicas` into the graph before executing the
temporary graph replica-update operation seam. In a real build-server flow,
the image upload or push to the registry happens before the deployment call.
The deployment request names the immutable uploaded image tag, then the
orchestrator materializes runtime replicas for the produced revision and
records the routing-remap milestones that move endpoint-bearing services
toward the new replica version.

Future on-premise validation should cover the same image-update flow through
the Resource Manager UI after resources have been created through the UI, with
identity and access enforcement enabled. That scenario is where registry
credentials, authorized image updates, and managed container app deployment
should be tested together.
