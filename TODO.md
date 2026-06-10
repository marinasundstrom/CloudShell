# TODO

This is the current task queue. Keep `docs/progress.md` as the living
tracker for completed decisions and broader MVP status.

See also: [Roadmap](docs/roadmap.md) for product direction and
[Progress](docs/progress.md) for completed work, current MVP focus, and the
verification baseline.

## Resource Management

- Continue settings and secrets integration by preserving configuration-entry
  and secret references in resource template export/import tests and behavior.
- Evaluate splitting the configuration provider registration into
  `AddConfigurationProvider()` for configuration stores and
  `AddSecretsProvider()` for Secrets Vault resources and secret resolvers,
  keeping compatibility for hosts that currently call `AddConfigurationProvider()`.
- Add a separate secrets client/provider for applications that load secrets
  in-process instead of receiving them through resource-assigned environment
  variables.
- Harden macOS host-provided virtual networking by exercising real local proxy
  mappings end to end, improving action capability reasons, and deciding how
  reconciled mappings should be persisted or stopped.
- Continue load balancer support beyond the first Traefik file-config provider
  by adding provider validation diagnostics, configuration preview, and richer
  host/runtime capability checks.
- For Traefik container mode, model implementation containers as provider-owned
  runtime state or child resources tied to the load balancer lifecycle on a
  selected host resource, not as user-authored container app resources.
- Continue the remote Docker hosts proposal beyond the first host-model slice:
  persist provider-owned UI host configuration, wire supported credential
  transports into Docker client creation, and add remote container action
  integration coverage against a testable Docker endpoint.
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
  host resolution, and how host-discovered containers map back to the app.
- Expand host-readiness warnings so endpoint mappings can name the specific
  missing gateway, load balancer, DNS, service mesh, firewall, or cluster
  network controller capability.
- Extend endpoint assignment conflict diagnostics beyond platform-owned
  endpoints so provider-projected runtime endpoints can participate in a
  Resource Manager-wide validation pass.
- Design provider-originated resource change streams so providers such as
  Docker can push discovered container/status changes into Resource Manager
  instead of relying only on UI-side inventory polling.

## Resource Manager Stabilization

No queued items.
