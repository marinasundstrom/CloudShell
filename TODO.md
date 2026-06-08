# TODO

This is the current task queue. Keep `docs/progress.md` as the living
tracker for completed decisions and broader MVP status.

See also: [Progress](docs/progress.md) for completed work, current MVP focus,
verification baseline, and broader priorities.

## Resource Management

- Extend workload and service registration UI to request endpoint assignment
  modes from the default network instead of only collecting direct port fields.
- Design provider selection and reconciliation for endpoint mappings when a
  networking-capable resource such as a gateway, load balancer, DNS publisher,
  or custom controller materializes the mapping.
- Add a build-action sample that pushes a container image to a registry and
  calls the authenticated Container Apps revision API with the immutable tag.
- Design container app revision history as commits of container app
  configuration changes, including image, registry, trigger, and runtime
  rollout metadata.
- Design container app replicas and scaling before expanding the current
  numeric field, including whether scaling implies load balancing, routing,
  service endpoints, observed runtime instances, scaling events, and provider
  state reporting.
- Persist resource events and expose event filtering by event type, actor, and
  time range.
- Define container app runtime instances/replicas separately from the stable
  container app resource, including explicit container host binding, default
  engine resolution, and how engine-discovered containers map back to the app.

## Resource Manager Stabilization

No queued items.
