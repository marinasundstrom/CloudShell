# TODO

This is the current task queue. Keep `docs/progress.md` as the living
tracker for completed decisions and broader MVP status.

See also: [Roadmap](docs/roadmap.md) for product direction and
[Progress](docs/progress.md) for completed work, current MVP focus, and the
verification baseline.

## Resource Management

- Harden macOS host-provided virtual networking by exercising real local proxy
  mappings end to end, improving action capability reasons, and deciding how
  reconciled mappings should be persisted or stopped.
- Implement the load balancer resource proposal with Traefik as the first
  provider target, using HTTP host/path and TCP routes mapped to stable target
  endpoints.
- Design provider-owned replication projection for resources that can implement
  replicas, keeping stable resources separate from runtime instances.
- Design container app revision history as commits of container app
  configuration changes, including image, registry, trigger, and runtime
  rollout metadata.
- Design container app replicas and scaling before expanding the current
  numeric field, including whether scaling implies load balancing, routing,
  service endpoints, observed runtime instances, scaling events, and provider
  state reporting.
- Evaluate whether `ResourceDefinition` and `Deployment` should become
  first-class concepts for desired configuration, applied runtime state,
  revision history, and rollouts.
- Evaluate whether container app isolation needs a container application
  environment resource after virtual networking and load balancing are working.
- Persist resource events and expose event filtering by event type, actor, and
  time range.
- Define container app runtime instances/replicas separately from the stable
  container app resource, including explicit container host binding, default
  engine resolution, and how engine-discovered containers map back to the app.
- Expand host-readiness warnings so endpoint mappings can name the specific
  missing gateway, load balancer, DNS, service mesh, firewall, or cluster
  network controller capability.

## Resource Manager Stabilization

No queued items.
