# Proposals

Proposals are living design documents meant to track the progress of a feature.
Their status, remaining tasks, open questions, and relationship to the current
roadmap should be kept updated as implementation work lands.

When a proposal changes direction or an implementation slice completes, update
the relevant proposal together with [Roadmap](../roadmap.md) and
[Progress](../progress.md). The repo-local Codex skills also require this
synchronization for feature and stabilization work.

## Proposal Status

This table is the authoritative proposal status index. Keep it current whenever
proposal status, MVP relevance, implementation order, or remaining work
changes. Milestone scope remains authoritative in [Roadmap](../roadmap.md).

| Order | Proposal | Status | Milestone relationship | Notes |
| --- | --- | --- | --- | --- |
| 1 | [Platform foundations](core/platform-foundations.md) | In progress | MVP: UX polish; Samples should work | Current MVP convergence focus. Tracks cross-cutting reliability, diagnostics, authorization, persistence, traceability, samples, and Resource Manager polish. |
| 2 | [Secrets management](services/secrets-management.md) | In progress | MVP: App settings and secrets integrations | Built-in vault and reference flow exist; remaining work focuses Resource Manager assignment polish, safe reference display, diagnostics, and identity-backed access where needed. |
| 3 | [Identity and access](core/identity-and-access.md) | Current implementation working document | MVP: Identity, Built-in; external validation | Built-in provisioning, scoped tokens, grants, and service integration are working. A Keycloak sample now validates external OIDC user sign-in, role claim mapping, and sample-scoped resource identity provisioning. Runtime credential delivery and token claim mapping for protected service calls remain open. |
| 4 | [Lifecycle orchestration](core/lifecycle-orchestration.md) | Proposed | MVP: Resource Manager behavior and traceability | Defines the common lifecycle action procedure, dependency execution flow, resource events, failure semantics, and future event-triggered extension point. |
| 5 | [Logging infrastructure](core/logging-infrastructure.md) | In progress | MVP: Traceability and Resource Manager UX polish | Provider logs now have additive structured metadata fields using familiar logging and OpenTelemetry terminology. Remaining work tracks audit schemas, retention, metrics, traces, diagnostics, and future non-text payloads. |
| 6 | [Container host abstraction](containers/container-host-abstraction.md) | In progress | MVP: Container Apps, Version 1 | Host descriptor/provider layer, shared resolver, Control Plane validation, Docker Compose resolver use, canonical host naming, missing-host action capability reasons, and basic host readiness/capability diagnostics are in place. Next work is credential readiness diagnostics and provider-owned runtime infrastructure. |
| 7 | [Virtual network resource](networking/virtual-network-resource.md) | In progress | MVP: Network primitives | Core model and host-local path exist; next MVP work is UI-managed public endpoint exposure, provider diagnostics, and integration with service/name mapping. |
| 8 | [Load balancer resource](networking/load-balancer-resource.md) | In progress | MVP: Network primitives | First Traefik/file-provider slice exists; remaining work focuses provider selection, validation, host readiness, lifecycle, and public endpoint UX. |
| 9 | [DNS and name mapping](networking/dns-and-name-mapping-resource.md) | Proposed | MVP: Services, discovery, and names; provider-backed DNS post-MVP | MVP should model logical names and UI relationships. Provider-backed DNS propagation and network-level service registries remain post-MVP unless needed by a concrete validation sample. |
| 10 | [Storage and volume mappings](storage/volume-mappings.md) | In progress | MVP: Storage and volume mappings | Initial Local Storage resource kind under `ResourceClass.Storage`, FileSystem medium projection, `cloudshell.volume`, `AddVolume(...)`, `ResourceVolumeMount`, mount permissions, container app selector/workload projection, dedicated Storage tab, direct volume UI, and storage-owned volume work are in place. Next work is runtime materialization, host/storage-medium compatibility diagnostics, relationship visibility, and usage monitoring. |
| 11 | [Remote Docker hosts](containers/remote-docker-hosts.md) | Partially implemented | MVP: Container Apps, Version 1 | Concrete remote-host work continues on top of the shared host abstraction, but should not block the local/default container-host MVP path. |
| 12 | [Provider-created and runtime-managed resources](core/provider-created-and-runtime-managed-resources.md) | In progress design | Post-MVP foundation, with MVP implications for diagnostics and cleanup | Decide ownership, visibility, cleanup, diagnostics, and authorization before broad runtime artifact projection. |
| 13 | [Deployments and revisions](deployment/deployments-and-revisions.md) | In progress design | Post-MVP foundation, with MVP current-revision support | Rich rollout history waits until runtime ownership and traceability boundaries are clear. |
| 14 | [Deployment projection](deployment/deployment-projection.md) | In progress | Later portability | Tracks external deployment artifact projection and should not displace the MVP control-plane milestone. |

## Current proposal order

Work proposal areas in the current product order:

1. Application environment management path across container apps, services,
   discovery, virtual networks, public endpoints, load balancers, and logical
   DNS/name mapping through [Virtual network resource](networking/virtual-network-resource.md),
   [Load balancer resource](networking/load-balancer-resource.md), and
   [DNS and name mapping](networking/dns-and-name-mapping-resource.md)
2. Stateful application foundation through
   [Storage and volume mappings](storage/volume-mappings.md)
3. Identity validation beyond the built-in provider through
   [Identity and access](core/identity-and-access.md), including a third-party
   OIDC/OAuth validation sample
4. MVP convergence and Resource Manager reliability through
   [Platform foundations](core/platform-foundations.md)
5. [Configuration and secrets access](services/secrets-management.md) with
   targeted [identity and access](core/identity-and-access.md) polish
6. [Lifecycle orchestration](core/lifecycle-orchestration.md) and
   [traceability/logging infrastructure](core/logging-infrastructure.md)
7. Host and runtime foundation through
   [Container host abstraction](containers/container-host-abstraction.md)
8. [Remote Docker host completion](containers/remote-docker-hosts.md), behind
   the local/default host path
9. Runtime ownership decisions through
   [Runtime-managed resources](core/provider-created-and-runtime-managed-resources.md)
10. [Deployments and revisions](deployment/deployments-and-revisions.md)
11. Advanced app and environment concepts

Use [Roadmap](../roadmap.md) for the reasoning behind this order and the
concrete MVP execution plan.
