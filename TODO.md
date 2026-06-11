# TODO

This is the current task queue. Keep `docs/progress.md` as the living
tracker for completed decisions and broader MVP status.

See also: [Roadmap](docs/roadmap.md) for product direction and
[Progress](docs/progress.md) for completed work, current MVP focus, and the
verification baseline.

## Immediate Proposal Order

Work the current proposals in this order:

1. Resource identity and permissions
2. Host abstractions
3. Configuration and secrets access
4. Traceability and audit
5. Remote Docker host completion
6. Provider-owned runtime lifecycle
7. Network and routing hardening
8. Runtime-managed resources
9. Deployments and revisions
10. Advanced app and environment concepts

## Now: Resource Identity and Permissions

- Keep `docs/resource-identity-and-permissions.md` as the current-state
  feature documentation and the matching proposal as the open-work tracker.
- Extend default resource identity-provider selection beyond the new provider
  catalog, including inheritance from resource groups or parent resources where
  the first model needs it. Public identity provider and binding projection
  contracts are now available on resources and the Control Plane API, and
  Control Plane hosts can configure provider registrations through
  `ResourceIdentity`.
- Add a replaceable development identity-provider path by hosting a separate
  reference identity server instance that speaks standard OIDC and OAuth 2.0.
  Treat it as development infrastructure, not as the CloudShell identity domain
  model, so teams can replace it with another standards-compliant provider.
- Decide whether a mock/development identity-provider mode needs behavior
  beyond the current programmatic identity declaration and unresolved-provider
  diagnostics path for authentication-disabled local development, including
  simulated user or workload principals for permission-boundary tests.
- Make the same identity-provider contract work with Microsoft Entra ID
  (Azure AD), including issuer/audience validation, claim mapping, groups or
  app roles, and client-credentials/service-principal flows for automation.
- Extend the new single-identity authoring API only where the first provider or
  permission-grant scenario proves it is needed, including possible multiple
  identities per resource.
- Define the resource permission foundation: permission assignments,
  permission inheritance boundaries, token claim mapping, workload identity
  lifecycle, and provider or orchestrator identities. Standard resource action
  permissions now use Azure-style operation names with `resources.manage` as a
  compatibility superset.
- Continue assigning and documenting specific Azure-style operation permissions
  per resource type or class. Load-balancer apply and network endpoint
  reconciliation now use documented network operation permissions instead of
  the generic custom-action execute permission.
- Continue resource action authorization beyond lifecycle actions so Resource
  Manager evaluates permissions before configuration updates, deployment
  operations, logs, diagnostics, and provider actions.
- Wire the identity contract into one provider-backed workload type so the
  model is validated against a concrete resource path.
- Add authorization diagnostics and capability reasons for denied or
  unavailable actions without leaking provider-specific internals.

## Next: Host Abstractions

- Implement the container host abstraction design next: add host-oriented
  descriptors, compatibility adapters for existing container-engine contracts,
  and a shared default/explicit host resolver.
- Migrate Docker Compose and default container-app host resolution to the
  shared resolver while preserving existing `ContainerEngineId` compatibility.
- Add host-resolution diagnostics and action capability reasons for missing
  hosts, unavailable hosts, missing credentials, and unsupported runtime
  capabilities.

## Next: Configuration, Secrets, and Audit

- Add Resource Manager UI support for assigning literal app settings,
  configuration-entry references, and vault-backed secret references on
  resources that advertise the environment-variable capability.
- Add a separate secrets client/provider for applications that load secrets
  in-process instead of receiving them through resource-assigned environment
  variables, using resource identity and permissions for secret access.
- Persist resource events and expose event filtering by event type, actor, and
  time range.
- Define audit event schemas for resource actions, host/runtime operations,
  image deployments, authorization decisions, and secret access.

## Next: Concrete Host and Runtime Foundation

- Continue the remote Docker hosts proposal on top of the shared host model:
  persist provider-owned UI host configuration, wire supported credential
  transports into Docker client creation, and keep credentials out of projected
  attributes, endpoints, logs, and diagnostics.
- Complete duplicate-host validation across local and remote Docker
  registration paths, including compatibility coverage for existing
  `docker.engine` registrations and stable `docker.host` UI/API projection.
- Verify remote-host container discovery, actions, and diagnostics end to end
  against a testable Docker endpoint with credential redaction coverage.
- Add provider-owned Docker runtime support for owner-scoped implementation
  containers after the resolver lands.
- Continue Traefik container mode beyond apply-time startup by tying the
  implementation container to load-balancer start, stop, delete, probe, and
  cleanup on the selected host resource.
- Extend app-owned ingress infrastructure with stop/delete lifecycle
  projection, provider-owned status, and diagnostics for replicated HTTP/TCP
  endpoints.

## Next: Network and Routing Hardening

- Harden macOS host-provided virtual networking by exercising real local proxy
  mappings end to end, improving action capability reasons, and deciding how
  reconciled mappings should be persisted or stopped.
- Expand host-readiness warnings so endpoint mappings can name the specific
  missing gateway, load balancer, DNS, service mesh, firewall, or cluster
  network controller capability.
- Finish provider-backed endpoint mapping materialization for real host
  networking services, not just logical local networking.
- Continue load balancer support beyond the first Traefik file-config provider
  by adding provider validation diagnostics, configuration preview, route
  conflict checks, target resolution diagnostics, and richer host/runtime
  capability checks.
- Finish the provider-resource selection path so load-balancer
  `UseProvider(...)`, explicit host selection, and UI-created resources behave
  consistently.
- Extend endpoint assignment conflict diagnostics beyond platform-owned
  endpoints so provider-projected runtime endpoints can participate in a
  Resource Manager-wide validation pass.
- Complete the `cloudshell.service` resource story for outward exposure,
  including its relationship to load balancers, service discovery, gateways,
  and Kubernetes-style service projection without making it the internal
  replica-management abstraction.

## Later: Runtime Ownership and Deployment Model

- Decide which runtime artifacts become runtime-managed resources versus
  provider-owned state: replicas, implementation containers, images, endpoint
  registrations, backend registrations, health probes, and revisions.
- Define ownership, visibility, query, authorization, cleanup, and
  garbage-collection rules for runtime-managed resources.
- Design provider-originated resource change streams so providers such as
  Docker can push discovered container/status changes into Resource Manager
  instead of relying only on UI-side inventory polling.
- Design provider-owned replication projection for resources that can implement
  replicas, keeping stable resources separate from runtime instances and using
  parent-derived naming conventions for materialized runtime containers.
- Define container app runtime instances/replicas separately from the stable
  container app resource, including explicit container host binding, default
  host resolution, and how host-discovered containers map back to the app.
- Harden `ResourceOrchestratorService` runtime mapping for replicated
  container apps, including Kubernetes Service/Deployment mapping,
  provider-observed replica health, traffic weights, generated diagnostics, and
  richer dynamic backend-pool behavior while keeping runtime artifacts
  implementation details below the container app resource.
- Design container app revision history as commits of container app
  configuration changes, including image, registry, trigger, runtime rollout
  metadata, retention, rollback, and failure handling.
- Evaluate whether `ResourceDefinition` and `Deployment` should become
  first-class concepts for desired configuration, applied runtime state,
  revision history, and rollouts after runtime ownership and traceability are
  defined.

## Later: Advanced App and Environment Concepts

- Design container app scaling beyond the current explicit replica-count API,
  including whether scaling implies load balancing, routing, service endpoints,
  observed runtime instances, scaling events, and provider state reporting.
- Extend container app ingress beyond the default replicated HTTP/TCP path with
  explicit ingress settings, TLS/certificate binding, host rules, richer
  health-aware backend selection, and traffic splitting.
- Complete the backend-pool and load-balancer integration path so virtual
  networks can express stable clustered routing behavior.
- Decide how Docker-discovered container resources should expose grouping
  metadata for runtime containers, including manually assigned groups and
  Docker Compose project/service labels, when those containers are shown as
  sub-items under a Docker host.
- Evaluate whether container app isolation needs a container application
  environment resource after host, routing, identity, runtime ownership, and
  deployment decisions are stable.

## Resource Manager Stabilization

No queued items.
