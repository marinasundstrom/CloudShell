# Replicated Container Health Sample

This sample declares a replicated ASP.NET Core container application with HTTP
health and liveness probes.

The stable `application:api` resource owns the workload declaration, service
endpoint, and replica count. The hidden runtime replica resources own the
materialized probe targets while the app is active. CloudShell polls those
runtime liveness observations and health-check signals, then materializes an
aggregate health assessment on the stable container app resource.

The sample also declares `docker:container:replicated-health-registry` as a
local registry at `localhost:5024` and configures the Docker host and the
project-container app to use that same registry. The ASP.NET Core project is
published as:

```text
localhost:5024/cloudshell-application-api:20260622.1
```

The tag is intentionally explicit so repeated replica-health runs use a
predictable image reference.

The sample keeps the app stopped by default so the resource model, Health tab,
and Control Plane health endpoints can be tested without requiring Docker to
start the containers. Start the Docker host and then the `api` resource to
publish the app ingress on `http://localhost:5092` and separate probe-only
ports for each runtime replica.

Override the registry port when `5024` is unavailable:

```bash
ReplicatedContainerHealth__RegistryPort=18024 \
  dotnet run --project samples/ReplicatedContainerHealth/CloudShell.ReplicatedContainerHealth.csproj -- --urls http://localhost:5008
```

When running the replica health path against Docker, start the registry
resource first, then start the `api` resource. The app start builds and pushes
the project container image to the configured registry before creating the
replicas.
