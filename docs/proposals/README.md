# Proposals

Proposals are living design documents meant to track the progress of a feature.
Their status, remaining tasks, open questions, and relationship to the current
roadmap should be kept updated as implementation work lands.

When a proposal changes direction or an implementation slice completes, update
the relevant proposal together with [Roadmap](../roadmap.md),
[ADR](../../ADR.md), and [Changelog](../../CHANGELOG.md). Durable decisions
belong in ADR; landed implementation changes belong in the changelog. The
repo-local Codex skills also require this synchronization for feature and
stabilization work.

## Proposal Status

This table is the authoritative proposal status index. Keep it current whenever
proposal status, MVP relevance, implementation order, or remaining work
changes. Milestone scope remains authoritative in [Roadmap](../roadmap.md).

| Order | Proposal | Status | Milestone relationship | Notes |
| --- | --- | --- | --- | --- |
| 1 | [Platform foundations](core/platform-foundations.md) | In progress | MVP: UX polish; Samples should work | Current MVP convergence focus. Tracks cross-cutting reliability, diagnostics, authorization, persistence, traceability, samples, and Resource Manager polish. |
| 2 | [Secrets management](services/secrets-management.md) | In progress | MVP: App settings and secrets integrations | Built-in vault and reference flow exist; remaining work focuses Resource Manager assignment polish, safe reference display, diagnostics, and identity-backed access where needed. |
| 3 | [Identity and access](core/identity-and-access.md) | Current implementation working document | MVP: Identity, Built-in; external validation | Built-in provisioning, scoped tokens, grants, and service integration are working. A Keycloak sample validates external OIDC user sign-in, role claim mapping, sample-scoped resource identity provisioning, runtime credential injection, provider setup, external service-bearer validation plumbing, and automated workload smoke coverage with a Keycloak-issued token. |
| 4 | [Lifecycle orchestration](core/lifecycle-orchestration.md) | Proposed | MVP: Resource Manager behavior and traceability | Defines the common lifecycle action procedure, dependency execution flow, resource events, failure semantics, and future event-triggered extension point. |
| 5 | [Logging infrastructure](core/logging-infrastructure.md) | In progress | MVP: Traceability and Resource Manager UX polish | Provider logs now have additive structured metadata fields using familiar logging and OpenTelemetry terminology. Remaining work tracks audit schemas, retention, metrics, traces, diagnostics, and future non-text payloads. |
| 6 | [Container applications](containers/container-applications.md) | In progress | MVP: Container Apps, Version 1; Application exposure, discovery, and names | Tracks `application.container-app` as the managed-service resource: image/revision, replicas, endpoints, service discovery, identity, storage, observability, app-owned ingress, and exposure relationships. |
| 7 | [Container host abstraction](containers/container-host-abstraction.md) | In progress | MVP: Container Apps, Version 1 | Host descriptor/provider layer, shared resolver, Control Plane validation, Docker Compose resolver use, canonical host naming, missing-host action capability reasons, and basic host readiness/capability diagnostics are in place. Next work is credential readiness diagnostics and provider-owned runtime infrastructure. |
| 8 | [Virtual network resource](networking/virtual-network-resource.md) | In progress | MVP: Network primitives | Core model and host-local path exist; next MVP work is UI-managed public endpoint exposure, provider diagnostics, and integration with service/name mapping. |
| 9 | [Load balancer resource](networking/load-balancer-resource.md) | In progress | MVP: Network primitives | First Traefik/file-provider slice exists; remaining work focuses provider selection, validation, host readiness, lifecycle, and public endpoint UX. |
| 10 | [DNS and name mapping](networking/dns-and-name-mapping-resource.md) | In progress | MVP: Application exposure, discovery, and names; provider-backed DNS post-MVP | First logical DNS zone and name-mapping projection exists through programmatic declarations. Resource Manager can create a DNS zone with one initial mapping and add standalone mappings to existing zones. Name-mapping update/delete UI, provider-backed DNS propagation, and network-level service registries remain open. |
| 11 | [Storage and volume mappings](storage/volume-mappings.md) | In progress | MVP: Storage and volume mappings | Initial Local Storage resource kind under `ResourceClass.Storage`, FileSystem medium projection, `cloudshell.volume`, `AddVolume(...)`, `ResourceVolumeMount`, mount permissions, container app selector/workload projection, dedicated Storage tab, direct volume UI, and storage-owned volume work are in place. Next work is runtime materialization, host/storage-medium compatibility diagnostics, relationship visibility, and usage monitoring. |
| 12 | [Remote Docker hosts](containers/remote-docker-hosts.md) | Partially implemented | MVP: Container Apps, Version 1 | Concrete remote-host work continues on top of the shared host abstraction, but should not block the local/default container-host MVP path. |
| 13 | [Provider-created and runtime-managed resources](core/provider-created-and-runtime-managed-resources.md) | In progress | MVP foundation for container app diagnostics; broader runtime projection later | Resource source, management mode, visibility, owner, and cleanup metadata now project through Resource, API, remote client, and Resource Manager inventory filtering. Container apps now project desired replica/container children as hidden runtime-managed resources, and Resource Manager can opt into hidden/runtime-managed views separately; next work is provider-observed IDs, health, placement, and materialization diagnostics. |
| 14 | [Deployments and revisions](deployment/deployments-and-revisions.md) | In progress | MVP internal foundation, with current-revision support | Internal orchestrator deployment/revision data contracts exist for container app/provider/orchestrator use. Rich rollout history, rollback, retention, traffic splitting, and public management APIs remain deferred. |
| 15 | [Deployment projection](deployment/deployment-projection.md) | In progress | Later portability | Tracks external deployment artifact projection and should not displace the MVP control-plane milestone. |
| 16 | [Resource graph import and code generation](core/resource-graph-import.md) | Proposed | Later portability and advanced authoring | Tracks external file import into CloudShell graph drafts, starting with Docker Compose YAML, with generated programmatic declarations as the preferred first output. |

## Current proposal order

Work proposal areas in the current product order:

1. Application environment management path through
   [Container applications](containers/container-applications.md), app-owned
   exposure/discovery, virtual networks, public endpoints, load balancers, and
   logical DNS/name mapping through [Virtual network resource](networking/virtual-network-resource.md),
   [Load balancer resource](networking/load-balancer-resource.md), and
   [DNS and name mapping](networking/dns-and-name-mapping-resource.md). Keep
   `cloudshell.service` semantics bounded until
   [Deployments and revisions](deployment/deployments-and-revisions.md)
   clarifies the shared deployment/orchestrator service model.
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
11. Advanced app and environment concepts, including external-format resource
    graph import and code generation

Use [Roadmap](../roadmap.md) for the reasoning behind this order and the
concrete MVP execution plan.
