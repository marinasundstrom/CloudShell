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

Start with the resource identity and permissions proposal. The initial work
should define the resource identity-provider contract, default provider
selection, resource identity bindings, resource-scoped permission names,
permission assignments, workload identity lifecycle, token claim mapping, and
action authorization diagnostics.

For development, CloudShell should host a separate reference identity server
instance that speaks standard OIDC and OAuth 2.0. That instance is development
infrastructure, not the CloudShell identity domain model. The same contracts
must work with Microsoft Entra ID (Azure AD) and allow teams to replace the
development server with Keycloak, Auth0, Okta, or another standards-compliant
provider later.

Isolated local development should also allow authentication to be disabled
while resources still declare and project mock identity bindings
programmatically, so apps can start locally and later switch those bindings to
Microsoft Entra ID or another production provider before publishing.

This phase should also establish the audit hooks required to explain allow and
deny decisions. Full policy engines, wildcard permissions, and advanced
inheritance can wait, but the first model must be strong enough to secure
resource actions, secret access, provider operations, and future deployment
inspection.

References:

- [Identity and Permissions Proposal](proposals/identity-and-permissions.md)
- [Resource Identity and Permissions Proposal](proposals/resource-identity-and-permissions.md)
- [Authentication and authorization](authentication-and-authorization.md)
- [Platform Foundations Proposal](proposals/platform-foundations.md)

### 2. Host Abstractions

Goal: jump next to the shared host resolver and runtime contract after the
resource identity slice is stable.

Host abstraction work remains the next major implementation chain. Load
balancers, app ingress, remote Docker hosts, and future runtime-managed
resources all need the same answer to "which host should materialize this?"
and "how does provider-owned runtime state get created, probed, stopped, and
cleaned up?"

The first slice should add host descriptors, compatibility adapters for the
existing container-engine contracts, a shared explicit/default host resolver,
and diagnostics for missing or unsuitable hosts. Provider-owned runtime
containers should come after the resolver is in place.

References:

- [Container Host Abstraction Proposal](proposals/container-host-abstraction.md)
- [Remote Docker Hosts Proposal](proposals/remote-docker-hosts.md)
- [Load Balancer Resource Proposal](proposals/load-balancer-resource.md)
- [Container apps](resources/container-apps.md)

### 3. Configuration and Secrets Access

Goal: align secret consumption and in-process configuration with the identity
foundation.

The existing Secrets Vault and resource-assignment path can continue, but
in-process secret loading and service-to-service secret access should use the
identity and permission model from the first phase. A resource should not gain
secret read access solely because it references a secret.

References:

- [Secrets Management Proposal](proposals/secrets-management.md)
- [Identity and Permissions Proposal](proposals/identity-and-permissions.md)
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

- [Platform Foundations Proposal](proposals/platform-foundations.md)
- [Identity and Permissions Proposal](proposals/identity-and-permissions.md)
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

- [Remote Docker Hosts Proposal](proposals/remote-docker-hosts.md)
- [Container Host Abstraction Proposal](proposals/container-host-abstraction.md)
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
projection for app-owned ingress infrastructure.

References:

- [Load balancers](resources/load-balancers.md)
- [Load Balancer Resource Proposal](proposals/load-balancer-resource.md)
- [Container Host Abstraction Proposal](proposals/container-host-abstraction.md)
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

- [Virtual Network Resource Proposal](proposals/virtual-network-resource.md)
- [Networking](networking.md)
- [Load Balancer Resource Proposal](proposals/load-balancer-resource.md)
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

- [Runtime-Managed Resource Proposal](proposals/runtime-managed-resource.md)
- [Deployments and Revisions Proposal](proposals/deployments-and-revisions.md)
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

- [Deployments and Revisions Proposal](proposals/deployments-and-revisions.md)
- [Runtime-Managed Resource Proposal](proposals/runtime-managed-resource.md)
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
- [Virtual Network Resource Proposal](proposals/virtual-network-resource.md)
- [Load Balancer Resource Proposal](proposals/load-balancer-resource.md)
- [Hosting model](hosting-model.md)

## Tracking Work

The current task queue stays in [TODO](../TODO.md). Completed decisions and
verification expectations stay in [Progress](progress.md). Larger design
threads should live under [docs/proposals](proposals/).
