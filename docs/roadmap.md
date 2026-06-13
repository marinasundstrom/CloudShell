# Roadmap

This roadmap describes the next product direction for CloudShell. It links to
the detailed proposals and domain docs rather than duplicating every design
detail here.

CloudShell is moving toward an on-premise control plane: a resource shell where
teams can model applications first, then add environment infrastructure,
networking, deployment, and operational control as the solution grows.

## Current Foundation

The current foundation is a resource model with programmatic declarations,
Resource Manager UI, provider-owned configuration, and a Control Plane API.

Useful references:

- [Domain model](domain-model.md)
- [Resource capabilities](capabilities.md)
- [System design guidelines](system-design-guidelines.md)
- [Programmatic resources](programmatic-resources.md)
- [Control Plane API](control-plane-api.md)
- [CloudShell and Aspire](cloudshell-and-aspire.md)

## Authoritative Milestones

Milestones in this file are the authoritative product scope. Proposal status
tables, progress notes, and the execution plan below should stay aligned with
these milestones instead of redefining release scope independently.

### MVP

Goal: make CloudShell useful as a combined-hosted local and team-owned control
plane while preserving the split-hosting path.

MVP scope:

| Area | Required outcome |
| --- | --- |
| Container Apps, Version 1 | Container app resources can be declared, inspected, started, stopped, updated by image/revision, configured with replicas, and connected to the default container-host path. |
| Network primitives | Network resources, endpoint requests, endpoint mappings, load-balancer routes, and host-local networking provide enough routing to expose common container app scenarios with clear diagnostics. |
| Identity, Built-in | The built-in identity provider can provision resource identities, issue scoped resource-permission tokens, and enforce those permissions for Control Plane actions, configuration reads, and secret reads. |
| App settings and secrets integrations | App settings, configuration-entry references, and secret references work through programmatic declarations, Resource Manager assignment flows, runtime transfer, redaction, and authorization. |
| UX polish | Resource Manager common workflows are understandable, diagnostics are actionable, generated details are useful, and identity, configuration, secrets, networking, and app controls are discoverable without bespoke sample code. |
| Samples should work | Supported samples build and smoke-test, including combined hosting, split hosting, container host, settings and secrets, host virtual networking, load balancer, project references, and container app deployment. |

The execution plan and near-term roadmap below are the implementation order for
reaching this MVP and then expanding beyond it.

## MVP Execution Plan

This section is the current task queue. Keep it focused on implementation
slices that move the MVP forward; proposal documents remain the design
trackers, and [Progress](progress.md) remains the completed-work tracker.

### Immediate Proposal Order

Work the current proposals in this order. For MVP, implement only the slice
listed here before pulling in broader proposal work.

1. Resource identity and permissions: close built-in access enforcement,
   provisioning authorization, deny diagnostics, and remaining permission
   boundary tests.
2. Host abstractions: add host descriptors, compatibility adapters, a shared
   resolver, Docker Compose/default container-app migration, and missing-host
   diagnostics.
3. Configuration and secrets access: add Resource Manager assignment flows for
   literal settings, configuration-entry references, and vault-backed secret
   references.
4. Traceability and audit: persist/filter resource events and define audit
   schemas for the operations already in the MVP path while keeping provider
   logs, resource events, diagnostics, metrics, traces, and future non-text
   payloads as separate concerns.
5. Remote Docker host completion: finish concrete Docker host registration,
   credentials, duplicate validation, discovery, diagnostics, and actions on
   top of the shared resolver.
6. Provider-owned runtime lifecycle: start with owner-scoped Docker runtime
   support for Traefik/load-balancer implementation containers and app-owned
   ingress cleanup.
7. Network and routing hardening: tighten host-readiness, provider selection,
   route conflicts, endpoint conflicts, configuration preview, and backend
   diagnostics for the supported samples.
8. Runtime-managed resources: make only the MVP ownership, cleanup, and
   diagnostics decisions needed by provider-owned runtime and current
   revisions.
9. Deployments and revisions: preserve current app-owned revision projection;
   defer rich rollout history, rollback, retention, and deployment resources.
10. Advanced app and environment concepts: defer autoscaling, backend pools,
    traffic splitting, `cloudshell.service`, DNS/name mapping, external
    deployment projection, and container application environments.

### Now: Resource Identity and Permissions

- Keep [Resource identity and permissions](resource-identity-and-permissions.md)
  as the current-state feature documentation and
  [Identity and access](proposals/core/identity-and-access.md) as the open-work
  tracker.
- Continue authorization diagnostics beyond resource-action capability reasons,
  especially for configuration updates, deployment operations, logs,
  diagnostics, provider actions, and audit event payloads.
- Later UI enforcement should disable or hide Resource Manager operations
  based on the current user's permissions, while still explaining the missing
  permission in the same diagnostic style as Azure-style portals.
- Continue assigning documented Azure-style operation permissions for
  configuration updates, deployment operations, logs, diagnostics, provider
  actions, and future runtime-managed resources as those operations enter the
  MVP path.
- Provisioning-resource authorization boundaries now have focused coverage:
  provisioning requires permission on the provisioning resource and manage
  permission on the target resource, while status reads require read
  permission on both the target and provisioning resource.
- Defer broad IAM work unless it blocks the built-in identity MVP: resource
  group or parent-resource identity inheritance, multiple identities per
  resource, effective permission APIs, durable external authority
  reconciliation, and provider-native requested-versus-effective grants.
- Keep Microsoft Entra ID compatibility as a required contract target, but do
  not block MVP on a full Entra provider if the provider-neutral contract and
  compatibility tests are clear.

### Next: Host Abstractions

- Add host-oriented descriptors, provider contracts, host registration, and
  builder/settings names. These host-oriented names are now in place for
  declarations, samples, Resource Manager settings, Docker host descriptors,
  and resolver-backed orchestration.
- Implement a shared `IContainerHostResolver` over explicit resource
  descriptors, default host providers, and registered default host descriptors.
  This is in place for Control Plane container-workload validation.
- Migrate Docker Compose materialization to the resolver while preserving
  samples and declarations on the current host-selection model. Docker Compose
  now requires the shared resolver instead of duplicating host lookup.
- Return diagnostics and action capability reasons for host placement failures.
  Missing explicit/default container hosts now disable affected Run/Restart
  actions before orchestration dispatch. The shared resolver also reports
  unavailable host resources and missing required host capabilities, with
  container-image and container-build capability IDs wired into container
  workload validation. Missing credentials remain.
- Resolver tests now cover explicit host selection, preferred host selection,
  configured default host selection, registered default host descriptors, and
  missing-host, unavailable-host, and required-capability diagnostics.
  Continue tests for credential diagnostics when that state is implemented.

### Next: Configuration, Secrets, and Audit

- Add Resource Manager UI support for assigning literal app settings,
  configuration-entry references, and vault-backed secret references on
  resources that advertise environment-variable support. The generic
  Environment tab now edits application app settings and environment variables
  through provider-owned configuration hooks.
- Show saved references and diagnostics without displaying resolved secret
  values. Application overview now renders app-setting and environment-variable
  references as source labels and target references instead of resolved values
  or raw CloudShell reference strings, and shows basic target availability and
  identity-grant status.
- Verify assignment flows against identity-backed configuration and secret
  read authorization. Runtime resolution failures now use typed diagnostics
  and project as resource-action-unavailable API errors instead of generic
  operation failures. Resource action capabilities now preflight safe
  reference checks for missing referenced resources and identity grants before
  orchestration dispatch.
- Expose transient lifecycle state such as `Starting` while start/restart
  operations are in progress. Application resources now project a fresh
  provider-owned starting observation and fall back to stopped when that
  observation becomes stale.
- Persist resource events and expose filtering by event type, actor, and time
  range. The initial persistence/query slice is in place through
  `IResourceEventManager`; Resource Manager activity UI is next.
- Define audit event schemas for resource actions, host/runtime operations,
  image deployments, authorization decisions, identity provisioning, and secret
  access.
- Use [Logging infrastructure](proposals/core/logging-infrastructure.md) to
  track structured logging, non-text operational payloads, resource events,
  audit records, diagnostics, metrics, and traces without prematurely merging
  those concerns.

### Next: Concrete Host and Runtime Foundation

- Continue the remote Docker hosts proposal on top of the shared host model:
  persist provider-owned UI host configuration, wire supported credential
  transports into Docker client creation, and keep credentials out of
  projected attributes, endpoints, logs, and diagnostics.
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
- Define host/runtime recovery policy separately from host restart cleanup:
  detached container apps should be rediscovered through container host and
  stable workload identity, while crash restart/backoff behavior should be an
  orchestrator policy instead of a side effect of runtime-state recovery.

### Next: Network and Routing Hardening

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

### Later: Runtime Ownership and Deployment Model

- Decide which runtime artifacts become runtime-managed resources versus
  provider-owned state: replicas, implementation containers, images, endpoint
  registrations, backend registrations, health probes, and revisions.
- Define ownership, visibility, query, authorization, cleanup, and
  garbage-collection rules for runtime-managed resources.
- Design provider-originated resource change streams so providers such as
  Docker can push discovered container/status changes into Resource Manager
  instead of relying only on UI-side inventory polling.
- Design provider-owned replication projection for resources that can
  implement replicas, keeping stable resources separate from runtime instances
  and using parent-derived naming conventions for materialized runtime
  containers.
- Preserve container app current-revision projection for image updates; defer
  rich rollout history, rollback, retention, and first-class deployment
  resources until runtime ownership and traceability are clear.

### Later: Advanced App and Environment Concepts

- Defer container app autoscaling beyond the current explicit replica-count
  API.
- Defer backend pools, TLS binding, traffic splitting, advanced service
  exposure, DNS/name mapping, external deployment projection, and container
  application environments until host, routing, identity, runtime ownership,
  and deployment decisions are stable.

## Near-Term Roadmap

The next work should follow the product focus first, then proposal
dependencies. Identity and permissions are now the first focus because every
later on-premise control-plane feature needs a consistent answer for who is
acting, what they can do, how workloads authenticate to platform services, and
how those decisions are audited.

Several first slices are already in place: virtual-network resources, macOS
host-networking, load-balancer resources, Traefik file-provider output,
Docker host projection, Secrets Vault resources, app-owned container ingress,
and explicit container-app replica counts. The remaining roadmap turns those
slices into coherent security, host, networking, and deployment foundations.

### 1. Resource Identity and Permissions

Goal: define resource identity and authorization first, then use that slice as
the foundation for broader platform identity.

Start with the identity and access proposal. The initial work
should define the resource identity-provider contract, default provider
selection, resource identity bindings, resource-scoped permission names,
permission assignments, workload identity lifecycle, token claim mapping, and
action authorization diagnostics.

The built-in development authority should preserve resource-permission
pairing in token claims so a permission granted on one resource cannot combine
with a different resource claim during API authorization.

The first provider-selection contract should stay small: concrete identity
bindings resolve by provider ID, required-but-unresolved bindings resolve to a
default provider, and richer inheritance can follow when resource groups and
parent resources participate in identity policy.

For development, CloudShell should host a separate reference identity server
instance that speaks standard OIDC and OAuth 2.0. That instance is development
infrastructure, not the CloudShell identity domain model. The same contracts
must work with Microsoft Entra ID (Azure AD) and allow teams to replace the
development server with Keycloak, Auth0, Okta, or another standards-compliant
provider later.

Resources should be able to declare identity intent programmatically, either by
binding to a concrete provider identity or by declaring that the resource will
have an identity later. Isolated local development may also disable
authentication and use mock identity bindings, but that is only one development
path before switching to Microsoft Entra ID or another production provider.

This phase should also establish the audit hooks required to explain allow and
deny decisions. Full policy engines, wildcard permissions, and advanced
inheritance can wait, but the first model must be strong enough to secure
resource actions, secret access, provider operations, and future deployment
inspection.

References:

- [Identity and Access Proposal](proposals/core/identity-and-access.md)
- [Resource identity and permissions](resource-identity-and-permissions.md)
- [Authentication and authorization](authentication-and-authorization.md)
- [Platform Foundations Proposal](proposals/core/platform-foundations.md)

### 2. Host Abstractions

Goal: jump next to the shared host resolver and runtime contract after the
resource identity slice is stable.

Host abstraction work remains the next major implementation chain. Load
balancers, app ingress, remote Docker hosts, and future runtime-managed
resources all need the same answer to "which host should materialize this?"
and "how does provider-owned runtime state get created, probed, stopped, and
cleaned up?"

The first slice should add host descriptors, compatibility adapters for the
existing container-host contracts, a shared explicit/default host resolver,
and diagnostics for missing or unsuitable hosts. Provider-owned runtime
containers should come after the resolver is in place.

References:

- [Container Host Abstraction Proposal](proposals/containers/container-host-abstraction.md)
- [Remote Docker Hosts Proposal](proposals/containers/remote-docker-hosts.md)
- [Load Balancer Resource Proposal](proposals/networking/load-balancer-resource.md)
- [Container apps](resources/container-apps.md)

### 3. Configuration and Secrets Access

Goal: align secret consumption and in-process configuration with the identity
foundation.

The existing Secrets Vault and resource-assignment path can continue, but
in-process secret loading and service-to-service secret access should use the
identity and permission model from the first phase. A resource should not gain
secret read access solely because it references a secret.

References:

- [Secrets Management Proposal](proposals/services/secrets-management.md)
- [Identity and Access Proposal](proposals/core/identity-and-access.md)
- [Resource templates](resource-templates.md)
- [Programmatic resources](programmatic-resources.md)

### 4. Traceability and Audit

Goal: make resource changes, deployment triggers, authorization decisions, and
reconciliation outcomes explainable over time.

Resource events already define the platform traceability stream. The next
slice is persistence, filtering, event schemas, and audit linkage for resource
actions, image deployments, host/runtime operations, secret access, and
authorization decisions.

References:

- [Platform Foundations Proposal](proposals/core/platform-foundations.md)
- [Identity and Access Proposal](proposals/core/identity-and-access.md)
- [Logging infrastructure](proposals/core/logging-infrastructure.md)
- [Container apps](resources/container-apps.md#logs-and-events)

### 5. Remote Docker Host Completion

Goal: complete the concrete user-managed Docker host story on top of the
host-first model.

Docker is the first concrete container host provider. The remaining work is
not a new platform abstraction; it is registration, credential handling,
provider-owned persistence, duplicate-host validation, and end-to-end action
coverage for local and remote Docker hosts.

This should follow the shared host resolver so UI-created and declared Docker
hosts participate in the same placement and diagnostics model used by
container apps and provider-owned infrastructure.

References:

- [Remote Docker Hosts Proposal](proposals/containers/remote-docker-hosts.md)
- [Container Host Abstraction Proposal](proposals/containers/container-host-abstraction.md)
- [Domain model](domain-model.md)

### 6. Provider-Owned Runtime Lifecycle

Goal: make implementation containers and helper services lifecycle-managed
without turning them into user-authored resources.

Once hosts can be resolved consistently, add the owner-scoped runtime contract
for provider-owned infrastructure. Traefik container mode is the first concrete
consumer: the load-balancer resource remains the stable user-facing resource,
while the Traefik implementation container is provider-owned runtime state or
an optional diagnostic child.

This phase should also tighten stop/delete cleanup and runtime status
projection for app-owned ingress infrastructure. Keep workload crash recovery
separate from host restart reconciliation: providers report observed stopped or
failed state, while orchestrators decide restart, backoff, or provider-native
policy.

References:

- [Load balancers](resources/load-balancers.md)
- [Load Balancer Resource Proposal](proposals/networking/load-balancer-resource.md)
- [Container Host Abstraction Proposal](proposals/containers/container-host-abstraction.md)
- [Container apps](resources/container-apps.md)

### 7. Network and Routing Hardening

Goal: harden virtual networking, load balancing, and replicated app ingress
against real host and provider behavior.

The core model is now established: network resources own endpoint requests and
mappings; load balancers own provider-neutral routes; replicated container
apps own normal app ingress. The next step is validation and diagnostics:
host-readiness warnings, provider selection, route and endpoint conflict
reporting, configuration preview, backend resolution, and richer action
capability reasons.

Backend pools, health-aware target selection, TLS binding, and traffic
splitting should wait until the current routing and host-readiness paths are
reliable.

References:

- [Virtual Network Resource Proposal](proposals/networking/virtual-network-resource.md)
- [Networking](networking.md)
- [Load Balancer Resource Proposal](proposals/networking/load-balancer-resource.md)
- [Load balancers](resources/load-balancers.md)

### 8. Runtime-Managed Resources

Goal: decide and implement how provider-created runtime artifacts are owned,
visible, cleaned up, and inspected.

This should follow host/runtime lifecycle work. The first decision is whether
replicas, implementation containers, endpoint registrations, backend
registrations, images, and revisions are normal runtime-managed resources,
provider-owned state, or a mix of both. The immediate requirement is ownership
and diagnostics without cluttering normal Resource Manager views.

References:

- [Runtime-Managed Resource Proposal](proposals/core/provider-created-and-runtime-managed-resources.md)
- [Deployments and Revisions Proposal](proposals/deployment/deployments-and-revisions.md)
- [Domain model: Resource](domain-model.md#resource)

### 9. Deployment and Revision Model

Goal: add first-class rollout history only after ownership, traceability, and
runtime inspection boundaries are clear.

Container apps already project a current app-owned revision for image updates.
A richer deployment model should answer versioned configuration, rollout
history, rollback, failure handling, retention, and orchestrator-specific
runtime state. Do not introduce `Deployment` just to support host selection,
networking, or basic replica count updates.

References:

- [Deployments and Revisions Proposal](proposals/deployment/deployments-and-revisions.md)
- [Runtime-Managed Resource Proposal](proposals/core/provider-created-and-runtime-managed-resources.md)
- [Container apps](resources/container-apps.md#revisions)
- [Progress](progress.md)

### 10. Advanced App and Environment Concepts

Goal: evaluate higher-level isolation, scaling, service exposure, and
environment boundaries after the lower-level foundations are stable.

This is where container application environments, autoscaling, traffic
splitting, `cloudshell.service`, backend pools, Kubernetes-style service
projection, and richer multi-host policy belong. These concepts depend on the
host, routing, identity, runtime ownership, and deployment decisions above.

References:

- [Container apps](resources/container-apps.md)
- [Virtual Network Resource Proposal](proposals/networking/virtual-network-resource.md)
- [Load Balancer Resource Proposal](proposals/networking/load-balancer-resource.md)
- [Hosting model](hosting-model.md)

## Tracking Work

The current task queue and milestone scope stay in this roadmap. Completed
decisions and verification expectations stay in [Progress](progress.md).
Proposal statuses stay in [docs/proposals](proposals/).
