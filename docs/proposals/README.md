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
| 1 | [Identity and access](core/identity-and-access.md) | Current implementation working document | MVP: Identity, Built-in | Current first focus. Tracks resource identity providers, built-in provisioning, resource-scoped permissions, token claims, and authorization diagnostics. |
| 2 | [Container host abstraction](containers/container-host-abstraction.md) | In progress | MVP: Container Apps, Version 1 | Host descriptor/provider layer, shared resolver, Control Plane validation, Docker Compose resolver use, canonical host naming, missing-host action capability reasons, and basic host readiness/capability diagnostics are in place. Next work is credential readiness diagnostics and provider-owned runtime infrastructure. |
| 3 | [Secrets management](services/secrets-management.md) | In progress | MVP: App settings and secrets integrations | Built-in vault slice exists; remaining work aligns assignment UI and secret access with resource identity and permissions. |
| 4 | [Platform foundations](core/platform-foundations.md) | In progress | MVP: UX polish; Samples should work | Cross-cutting foundation tracker for identity, authorization, traceability, persistence, diagnostics, and operational reliability. |
| 5 | [Logging infrastructure](core/logging-infrastructure.md) | Proposed | MVP: Traceability and Resource Manager UX polish | Tracks the boundary between provider logs, resource events, audit records, diagnostics, metrics, traces, structured data, and future non-text payloads. |
| 6 | [Lifecycle orchestration](core/lifecycle-orchestration.md) | Proposed | MVP: Resource Manager behavior and traceability | Defines the common lifecycle action procedure, dependency execution flow, resource events, failure semantics, and future event-triggered extension point. |
| 7 | [Remote Docker hosts](containers/remote-docker-hosts.md) | Partially implemented | MVP: Container Apps, Version 1 | Concrete Docker host resource work continues on top of the shared host abstraction. |
| 8 | [Load balancer resource](networking/load-balancer-resource.md) | In progress | MVP: Network primitives | First Traefik/file-provider slice exists; remaining work focuses provider selection, validation, host readiness, and lifecycle. |
| 9 | [Virtual network resource](networking/virtual-network-resource.md) | In progress | MVP: Network primitives | Core model and host-local path exist; provider-backed routing and clustered behavior remain incomplete. |
| 10 | [Provider-created and runtime-managed resources](core/provider-created-and-runtime-managed-resources.md) | In progress design | Post-MVP foundation, with MVP implications for diagnostics and cleanup | Decide ownership, visibility, cleanup, diagnostics, and authorization before broad runtime artifact projection. |
| 11 | [Deployments and revisions](deployment/deployments-and-revisions.md) | In progress design | Post-MVP foundation, with MVP current-revision support | Rich rollout history waits until runtime ownership and traceability boundaries are clear. |
| 12 | [Deployment projection](deployment/deployment-projection.md) | In progress | Later portability | Tracks external deployment artifact projection and should not displace the MVP control-plane milestone. |
| 13 | [DNS and name mapping](networking/dns-and-name-mapping-resource.md) | Proposed | Later networking | Name resolution remains separate from MVP network primitives unless needed by a concrete routing slice. |

## Current proposal order

Work proposal areas in the current product order:

1. [Identity and access](core/identity-and-access.md)
2. [Host abstractions](containers/container-host-abstraction.md)
3. [Configuration and secrets access](services/secrets-management.md)
4. [Traceability, audit, and logging infrastructure](core/logging-infrastructure.md)
5. [Lifecycle orchestration](core/lifecycle-orchestration.md)
6. [Remote Docker host completion](containers/remote-docker-hosts.md)
7. Provider-owned runtime lifecycle
8. Network and routing hardening
9. [Runtime-managed resources](core/provider-created-and-runtime-managed-resources.md)
10. [Deployments and revisions](deployment/deployments-and-revisions.md)
11. Advanced app and environment concepts

Use [Roadmap](../roadmap.md) for the reasoning behind this order and the
concrete MVP execution plan.
