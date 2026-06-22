# Replicated Container Health Sample

This sample declares a replicated ASP.NET Core container application with HTTP
health and liveness probes.

The stable `application:api` resource owns the workload declaration, service
endpoint, and replica count. The hidden runtime replica resources own the
materialized probe targets while the app is active. CloudShell polls those
runtime liveness observations and health-check signals, then materializes an
aggregate health assessment on the stable container app resource.

The sample uses the Docker host's local image store by default. The ASP.NET
Core project is published as:

```text
cloudshell-application-api:20260622.1
```

The tag is intentionally explicit so repeated replica-health runs use a
predictable image reference. Configure a registry on the Docker host and pass
the same registry to `AsContainer(...)` only when the image must be pushed to a
separate target registry.

The sample keeps the app stopped by default so the resource model, Health tab,
and Control Plane health endpoints can be tested without requiring Docker to
start the containers. Start the Docker host and then the `api` resource to
publish the app ingress on `http://localhost:5092` and separate probe-only
ports for each runtime replica.

When running the replica health path against Docker, start the `api` resource.
The app start builds the project container image into the local Docker image
store before creating the replicas.
