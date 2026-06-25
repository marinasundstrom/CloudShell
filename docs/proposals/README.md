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

Use the [Refactoring tracker](../refactoring.md) for active cross-cutting
cleanup task lists. Proposals should retain feature shape and open questions;
the tracker should carry current refactoring slices, completed cleanup, and
near-term boundary work.

## Proposal Status

This table is the authoritative proposal status index. Keep it current whenever
proposal status, MVP relevance, implementation order, or remaining work
changes. Milestone scope remains authoritative in [Roadmap](../roadmap.md).

| Order | Proposal | Status | Milestone relationship | Notes |
| --- | --- | --- | --- | --- |
| 1 | [Platform foundations](core/platform-foundations.md) | In progress | MVP: UX polish; Samples should work | Current MVP convergence focus. Tracks the local development target with the UI foundation as the stabilization lens: consistent current UI structure, reusable components, iconography, maintainable Resource Manager and Settings patterns, Application Topology confidence, app-centric workflows, focused relationship comprehension, readiness diagnostics, readable resource labels, settings/secrets/identity polish, persisted-state handoff, and cross-cutting reliability. |
| 2 | [Secrets management](services/secrets-management.md) | In progress | MVP: App settings and secrets integrations | Built-in vault and reference flow exist; remaining work focuses Resource Manager assignment polish, safe reference display, diagnostics, and identity-backed access where needed. |
| 3 | [Identity and access](core/identity-and-access.md) | Current implementation working document | MVP: Identity, Built-in; external validation | Built-in provisioning, scoped tokens, grants, and service integration are working. A Keycloak sample validates external OIDC user sign-in, role claim mapping, sample-scoped resource identity provisioning, runtime credential injection, provider setup, external service-bearer validation plumbing, and automated workload smoke coverage with a Keycloak-issued token. |
| 4 | [Lifecycle orchestration](core/lifecycle-orchestration.md) | Proposed | MVP: Resource Manager behavior and traceability | Defines the common lifecycle action procedure, dependency execution flow, resource events, failure semantics, and future event-triggered extension point. |
| 5 | [Logging infrastructure](core/logging-infrastructure.md) | In progress | MVP: Traceability and Resource Manager UX polish | Provider logs now have additive structured metadata fields using familiar logging and OpenTelemetry terminology. Remaining work tracks audit schemas, retention, metrics, traces, diagnostics, and future non-text payloads. |
| 6 | [Resource monitoring](core/resource-monitoring.md) | In progress | MVP: Resource Manager UX polish; provider diagnostics | Tracks provider-observed resource metrics under the Management group. Current slices add the `monitoring` resource capability, provider-backed snapshots, Control Plane API/client support, a generated Monitoring tab, Docker container CPU/memory/network/block/process/restart/uptime metrics, local application/configuration/secrets service process CPU/memory/thread/uptime metrics, single-instance container-backed application stats, and a provider-owned container app Monitoring dashboard with app summaries and per-replica breakdowns when a static/default container host can be resolved. |
| 7 | [Resource recovery](core/resource-recovery.md) | Proposed | MVP: Recovery policy after lifecycle and health foundations | Tracks liveness-signal-driven automatic restart policy, exponential backoff, provider restart participation through standard lifecycle actions, Resource Manager configuration, and recovery diagnostics while keeping host restart cleanup separate from workload crash recovery. |
| 8 | [Service telemetry and degradation](core/service-observability-and-degradation.md) | Proposed | MVP: App telemetry and local degradation analysis | Tracks the service-first, replica-aware telemetry overview that correlates load, recent exceptions, logs, traces, health, telemetry metrics, resource monitoring, capacity context, and redacted public reports without turning CloudShell into a full observability platform. |
| 9 | [Container applications](containers/container-applications.md) | In progress | MVP: Container Apps, Version 1; Application exposure, discovery, and names | Tracks `application.container-app` as the managed-service resource: image/revision, replicas, endpoints, service discovery, identity, storage, observability, app-owned ingress, and exposure relationships. |
| 10 | [Container host abstraction](containers/container-host-abstraction.md) | In progress | MVP: Container Apps, Version 1 | Host descriptor/provider layer, shared resolver, Control Plane validation, Docker Compose resolver use, canonical host naming, missing-host action capability reasons, and basic host readiness/capability diagnostics are in place. Next work is credential readiness diagnostics and provider-owned runtime infrastructure. |
| 11 | [Virtual network resource](networking/virtual-network-resource.md) | In progress | MVP: Network primitives | Core model and host-local path exist; next MVP work is UI-managed public endpoint exposure, provider diagnostics, and integration with service/name mapping. |
| 12 | [Load balancer resource](networking/load-balancer-resource.md) | In progress | MVP: Network primitives | First Traefik/file-provider slice exists; remaining work focuses provider selection, validation, host readiness, lifecycle, and public endpoint UX. |
| 13 | [DNS and name mapping](networking/dns-and-name-mapping-resource.md) | In progress | MVP: Application exposure, discovery, and names; provider-backed DNS post-MVP | First logical DNS zone and name-mapping projection exists through programmatic declarations. Resource Manager can create a DNS zone with one initial mapping and add standalone mappings to existing zones. Name-mapping update/delete UI, provider-backed DNS propagation, and network-level service registries remain open. |
| 14 | [Storage and volume mappings](storage/volume-mappings.md) | In progress | MVP: Storage and volume mappings | Initial Local Storage resource kind under `ResourceClass.Storage`, FileSystem medium projection, `cloudshell.volume`, `AddVolume(...)`, `ResourceVolumeMount`, mount permissions, container app selector/workload projection, dedicated Storage tab, direct volume UI, storage-owned volume work, consumer mount observations, and storage/volume runtime status projection are in place. Next work is broader provider-backed materialization, richer host/storage-medium compatibility diagnostics, relationship visibility, and usage monitoring. |
| 15 | [Remote Docker hosts](containers/remote-docker-hosts.md) | Partially implemented | MVP: Container Apps, Version 1 | Concrete remote-host work continues on top of the shared host abstraction, but should not block the local/default container-host MVP path. |
| 16 | [Provider-created and runtime-managed resources](core/provider-created-and-runtime-managed-resources.md) | In progress | MVP foundation for container app diagnostics; broader runtime projection later | Resource source, management mode, visibility, owner, and cleanup metadata now project through Resource, API, remote client, and Resource Manager inventory filtering. Container apps now materialize requested replica resources as hidden runtime-managed children, and Resource Manager can opt into hidden/runtime-managed views separately; Docker host container discovery remains a provider-observed projection path. Next work is provider-observed IDs, health, placement, and materialization diagnostics. |
| 17 | [Orchestrator deployments and environment revisions](deployment/deployments-and-revisions.md) | In progress | MVP internal foundation, with environment-history support | Internal orchestrator deployment/environment-revision data contracts exist for provider/orchestrator use, with container apps as the first consumer. Rich rollout history, environment replay, retention, traffic splitting, and public management APIs remain deferred. |
| 18 | [Deployment projection](deployment/deployment-projection.md) | In progress | Later portability | Tracks external deployment artifact projection and should not displace the MVP control-plane milestone. |
| 19 | [UI composition library](core/ui-composition.md) | Current implementation working document | Post-MVP reusable UI foundation | Tracks the standalone `CloudShell.UI.Composition` and `CloudShell.UI.Composition.Blazor` library direction: generic graph primitives, typed IDs, modules, menus, pages, section containers, sections, route metadata, renderer hints, plain Blazor renderers, descriptor projection, and future graph persistence. This is separate from the CoreShell product experience and extension API. |
| 20 | [Shell composition](core/shell-composition.md) | Proposed | Post-MVP extensible shell platform | Tracks the future CoreShell direction above the UI composition library: formal main navigation, common Settings hierarchy, notifications, provider workspaces, documented extension areas, shell-owned validation, default Fluent UI presenters, and adapters from CloudShell product abstractions into generic composition primitives. The current MVP should consume the landed composition work only to stabilize existing shell, Settings, and Resource Manager surfaces. |
| 21 | [Resource Manager project structure](core/resource-manager-project-structure.md) | Proposed | Post-MVP UI and hosting structure | Tracks the desired logical and physical split between CoreShell infrastructure, CoreShell extension contracts, CoreShell Fluent UI presenters, the CloudShell product host, Resource Manager UI, Resource Manager UI abstractions, Resource Manager host installation, Control Plane services, and provider UI versus provider runtime integrations. |
| 22 | [Resource graph and runtime separation](core/resource-definitions-and-capability-providers.md) | POC in progress | Later authoring, persistence, and provider model foundation | Initial isolated `CloudShell.ResourceDefinitions` project proves the graph/configuration contract, definition envelope, inheritance resolver, diagnostics, and attached provider contracts without replacing the Control Plane runtime pipeline yet. |
| 23 | [Resource graph import and code generation](core/resource-graph-import.md) | Proposed | Later portability and advanced authoring | Tracks external file import into CloudShell graph drafts, starting with Docker Compose YAML, with generated programmatic declarations as the preferred first output. |
| 24 | [Managed SQL Server resource](resources/managed-sql-server.md) | Partially implemented | Post-MVP managed database resource shape | Tracks the future SQL Server managed resource surface. The current `application.sql-server` implementation remains a local-development container-backed bridge, but now has a provider-owned builder, projects as a service resource, displays declared database children, reports requested-versus-effective grant status, and avoids generic container-app deployment controls by default. |
| 25 | [IoT device provisioning](core/iot-device-provisioning.md) | Proposed future direction | Later device and edge integration | Tracks a future IoT/edge story where devices bootstrap with pre-issued credentials, are reconciled into the CloudShell resource graph, receive principals and service access through the existing identity/access model, and publish health, activity, and telemetry without requiring a separate Azure-like service catalog. |

## Current proposal order

Work proposal areas in the current product order:

For MVP work, pull from these proposals only when the slice improves the
current local app-development loop: sample confidence, app-centric Resource
Manager understanding, preflight diagnostics, runtime-impacting
configuration/secrets/identity clarity, persisted-state handoff, or release
hardening.

1. MVP convergence and Resource Manager reliability through
   [Platform foundations](core/platform-foundations.md), with the current UI
   foundation as the first stabilization lens. Keep supported samples green,
   make Application Topology the broad local-development proof, and stabilize
   the UI structures the MVP already uses: page anatomy, local navigation,
   generated details, selectors, tables, alerts, action controls, iconography,
   status/empty/loading/error states, and provider-owned views. Consolidate
   duplicated components and presenters when that improves maintainability,
   consistency, and testability. Do not turn this into broad shell-composition
   or extension API work before the internal Resource Manager and Settings
   surfaces are stable. Expect a deliberate refactoring step later when
   CloudShell starts building shell-owned abstractions and extension points on
   top of the reusable UI composition library; do not prematurely expose the
   current internal presenters as the extension contract.
2. Application environment management path through
   [Container applications](containers/container-applications.md), app-owned
   exposure/discovery, virtual networks, public endpoints, load balancers, and
   logical DNS/name mapping through [Virtual network resource](networking/virtual-network-resource.md),
   [Load balancer resource](networking/load-balancer-resource.md), and
   [DNS and name mapping](networking/dns-and-name-mapping-resource.md). Keep
   `cloudshell.service` semantics bounded until
   [Orchestrator deployments and environment revisions](deployment/deployments-and-revisions.md)
   clarifies the shared deployment/orchestrator service model.
3. Readiness diagnostics for already-supported start, update, storage,
   identity, route, DNS/name, and provider-reconcile paths.
4. [Configuration and secrets access](services/secrets-management.md) with
   targeted [identity and access](core/identity-and-access.md) polish
5. Stateful application foundation through
   [Storage and volume mappings](storage/volume-mappings.md)
6. Identity validation beyond the built-in provider through
   [Identity and access](core/identity-and-access.md), including a third-party
   OIDC/OAuth validation sample
7. [Lifecycle orchestration](core/lifecycle-orchestration.md),
   [traceability/logging infrastructure](core/logging-infrastructure.md),
   [resource monitoring](core/resource-monitoring.md),
   [resource recovery](core/resource-recovery.md), and
   [service telemetry and degradation](core/service-observability-and-degradation.md)
8. Host and runtime foundation through
   [Container host abstraction](containers/container-host-abstraction.md)
9. [Remote Docker host completion](containers/remote-docker-hosts.md), behind
   the local/default host path
10. Runtime ownership decisions through
   [Runtime-managed resources](core/provider-created-and-runtime-managed-resources.md)
11. [Orchestrator deployments and environment revisions](deployment/deployments-and-revisions.md)
12. [UI composition library](core/ui-composition.md),
    [Shell composition](core/shell-composition.md), and
    [Resource Manager project structure](core/resource-manager-project-structure.md),
    after Resource Manager and supported local-development samples are stable
    enough to generalize the reusable library primitives, CoreShell-owned
    shell experience contracts, and Resource Manager UI/hosting boundaries.
    During MVP convergence, only take composition or project-boundary work that
    fixes regressions or directly supports the current shell, Resource Manager,
    and Settings experience.
13. Advanced app and environment concepts, including formal resource
    definitions and capability providers, external-format resource graph import
    and code generation, IoT device provisioning, and edge/device resource
    management

Use [Roadmap](../roadmap.md) for the reasoning behind this order and the
concrete MVP execution plan.
