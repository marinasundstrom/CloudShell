# Replicated Container Health Sample

This sample declares a replicated container application with HTTP health and
liveness probes.

The stable `application:api` resource owns the workload declaration, endpoint,
and replica count. The hidden runtime replica resources are the concrete probe
targets. CloudShell polls those runtime liveness observations and health-check
signals, then materializes an aggregate health assessment on the stable
container app resource.

The sample keeps the app stopped by default so the resource model, Health tab,
and Control Plane health endpoints can be tested without requiring Docker to
start the containers.
