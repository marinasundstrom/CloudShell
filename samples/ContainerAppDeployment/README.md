# Container App Deployment Sample

This sample models a container app deployed next to a local registry resource.
It is intended to show the Control Plane deployment flow without requiring a
real build server.

The sample runs with the new Resource model provider path. The resource graph
declares:

- `docker.host:sample`: the local Docker environment.
- `docker.container:sample-registry`: a local registry instance at
  `localhost:5023`.
- `application.container-app:sample-api`: a container app that depends
  on the registry for lifecycle ordering and starts from the mock image tag
  `cloudshell/mock-api:20260608.1`.

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

The app declaration keeps the lifecycle dependency explicit:

- `DependsOn(registry)` records that the app should start after the local
  registry is available.

The Docker host, registry resource, and container app are declared
programmatically through `DefineResources(...)` and then grouped through the
Resource Manager adapter. The current sample keeps those declarations
host-bound; durable deployment/import semantics are deferred to the later
deployment-definition work.

## Resource model POC coverage

The sample no longer declares the old application/Docker provider resources.
The deployment and replica APIs update the Resource graph resource directly and
execute a sample-local runtime bridge that accepts the state change without
materializing a real container app runtime yet. This is a switch-readiness gate
for the API and graph apply path; real container-app materialization remains a
provider runtime follow-up.

The sample also includes an opt-in registry runtime materializer behind:

```bash
ContainerAppDeployment__EnableDockerRuntime=true
```

When enabled, `start`, `restart`, and `stop` operations for
`docker.container:sample-registry` create, recreate, and remove a local
Docker registry container named
`cloudshell-container-app-deployment-registry`. It is disabled by default
so normal sample projection and smoke coverage do not depend on Docker CLI
availability or latency. The Docker command runner is sample-local and covered
by deterministic command-construction tests plus Docker smoke
coverage that starts the registry, verifies `/v2/`, and stops/removes the
container without old provider records. The runtime also contributes
control-plane-scoped workload metadata so graceful host shutdown removes the
registry container through the host-scoped shutdown service. It is not
the durable Docker provider runtime implementation. Registry status projection
uses a bounded, cached Docker inspect probe so enabling the materializer does
not make normal Resource Manager rendering depend on a responsive Docker
daemon.

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
samples/ContainerAppDeployment/deploy-mock-image.sh application.container-app:sample-api 20260608.2
```

The script posts to:

```text
POST /api/container-apps/v1/{containerAppId}/deployments
```

with a new image tag. The `application.container-app:sample-api` path uses the
same API to apply `container.image` and optional `container.replicas` changes
into the Resource graph before executing the sample-local image-update runtime
bridge. This is a POC bridge for accepted graph changes; in the current API
path, image upload and deployment creation remain Control Plane workflows.
The app also supports the existing:

```text
PUT /api/container-apps/v1/{containerAppId}/replicas
```

route, which applies `container.replicas` into the graph before executing the
sample-local replica-update runtime bridge. In a real build-server flow,
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
